using System.Net;
using System.Text;
using System.Text.Json;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Infrastructure.Http;

public sealed class HttpOutboxDispatcher : IOutboxDispatcher
{
    private const int MaximumErrorBodyLength = 64 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpOutboxDispatcher> _logger;

    public HttpOutboxDispatcher(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpOutboxDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OutboxDispatchResultDto> DispatchAsync(
        ClaimedOutboxMessageDto message,
        CancellationToken cancellationToken = default)
    {
        var destination = ResolveDestination(message);
        if (destination is null)
        {
            _logger.LogWarning(
                "Outbox message {MessageId} has unknown destination {Destination}.",
                message.Id,
                message.Destination);

            return new OutboxDispatchResultDto(
                false,
                false,
                "Outbox.UnknownDestination",
                "Outbox destination is not configured.");
        }

        var (clientName, requestUri) = destination.Value;
        var client = _httpClientFactory.CreateClient(clientName);
        using var content = new StringContent(message.Payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(requestUri, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new OutboxDispatchResultDto(true, false, null, null);
        }

        var applicationError = await TryReadApplicationErrorAsync(
            response,
            cancellationToken);
        var retryable = applicationError?.Retryable
            ?? IsRetryableStatus(response.StatusCode);
        var errorCode = applicationError?.Code
            ?? $"Http.{(int)response.StatusCode}";

        return new OutboxDispatchResultDto(
            false,
            retryable,
            errorCode,
            $"Downstream delivery returned HTTP {(int)response.StatusCode}.");
    }

    private static (string ClientName, string RequestUri)? ResolveDestination(
        ClaimedOutboxMessageDto message)
    {
        if (message.Destination.Contains("Recommendation", StringComparison.OrdinalIgnoreCase))
        {
            var requestUri = message.EventType switch
            {
                "ProductSnapshotChangedV1" => "/api/v1/events/products",
                "OrderConfirmedForRecommendationV1" => "/api/v1/events/orders",
                _ => null
            };

            return requestUri is null ? null : ("RecommendationApi", requestUri);
        }

        if (message.Destination.Contains("ErpIntegration", StringComparison.OrdinalIgnoreCase)
            && message.EventType == "OrderReadyForErpV1")
        {
            return ("ErpIntegrationApi", "/api/v1/integration/orders");
        }

        return null;
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || numericStatus >= 500;
    }

    private static async Task<ApplicationError?> TryReadApplicationErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumErrorBodyLength)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length == 0 || body.Length > MaximumErrorBodyLength)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errorElement = TryGetProperty(root, "error", out var nestedError)
                && nestedError.ValueKind == JsonValueKind.Object
                    ? nestedError
                    : root;

            if (!TryGetProperty(errorElement, "code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var code = codeElement.GetString();
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            bool? retryable = null;
            if (TryGetProperty(errorElement, "retryable", out var retryableElement)
                && retryableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                retryable = retryableElement.GetBoolean();
            }

            return new ApplicationError(code, retryable);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record ApplicationError(string Code, bool? Retryable);
}
