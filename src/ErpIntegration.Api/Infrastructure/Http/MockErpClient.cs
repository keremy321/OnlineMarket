using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Infrastructure.Http;

public sealed class MockErpClient(
    HttpClient httpClient,
    IOptions<MockErpClientOptions> options,
    MockErpCircuitBreaker circuitBreaker,
    TimeProvider timeProvider,
    ILogger<MockErpClient> logger)
    : IMockErpClient
{
    public const string ApiKeyHeaderName = "X-Api-Key";
    public const string IdempotencyHeaderName = "Idempotency-Key";
    public const string CorrelationHeaderName = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly MockErpClientOptions options = options.Value;

    public async Task<StepExecutionResult> ExecuteAsync(
        ClaimedIntegrationStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        MockErpMappedRequest mapped;
        try
        {
            mapped = MockErpRequestMapper.Map(step);
        }
        catch (MockErpRequestMappingException exception)
        {
            var maskedRequest = JsonSerializer.Serialize(
                new
                {
                    operation = step.StepType.ToString()
                },
                JsonOptions);
            return new StepExecutionResult(
                IntegrationResultType.PermanentFailure,
                null,
                Convert.ToHexString(
                        SHA256.HashData(
                            Encoding.UTF8.GetBytes(maskedRequest)))
                    .ToLowerInvariant(),
                maskedRequest,
                null,
                exception.Code,
                exception.Message,
                null,
                null);
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            mapped.Payload,
            mapped.Payload.GetType(),
            JsonOptions);
        var requestHash = Convert.ToHexString(
                SHA256.HashData(payload))
            .ToLowerInvariant();

        for (var sendNumber = 0; ; sendNumber++)
        {
            var nowUtc = GetUtcNow();
            if (!circuitBreaker.TryAcquire(
                    nowUtc,
                    out var circuitRetryAfterUtc))
            {
                return TransientFailure(
                    requestHash,
                    mapped.MaskedPayload,
                    "MockErp.CircuitOpen",
                    "The Mock ERP circuit is temporarily open.",
                    null,
                    null,
                    circuitRetryAfterUtc);
            }

            StepExecutionResult result;
            try
            {
                using var request = CreateRequest(step, mapped.Path, payload);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                result = await ReadResultAsync(
                    step.StepType,
                    mapped,
                    requestHash,
                    response,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                result = TransientFailure(
                    requestHash,
                    mapped.MaskedPayload,
                    "MockErp.Timeout",
                    "The Mock ERP request timed out.");
            }
            catch (HttpRequestException)
            {
                result = TransientFailure(
                    requestHash,
                    mapped.MaskedPayload,
                    "MockErp.ConnectionFailure",
                    "The Mock ERP connection failed.");
            }

            if (result.Succeeded)
            {
                circuitBreaker.RecordSuccess();
                logger.LogInformation(
                    "Mock ERP operation {Operation} succeeded with status {StatusCode} for correlation {CorrelationId}.",
                    mapped.Operation,
                    result.HttpStatusCode,
                    step.CorrelationId);
                return result;
            }

            if (result.ResultType == IntegrationResultType.PermanentFailure)
            {
                circuitBreaker.RecordSuccess();
                logger.LogWarning(
                    "Mock ERP operation {Operation} failed permanently with code {ErrorCode} for correlation {CorrelationId}.",
                    mapped.Operation,
                    result.ErrorCode,
                    step.CorrelationId);
                return result;
            }

            circuitBreaker.RecordTransientFailure(
                GetUtcNow(),
                options.CircuitBreakerFailureThreshold,
                options.CircuitBreakerBreakDuration);
            var fastRetryDelay = GetFastRetryDelay(
                sendNumber,
                result.RetryAfterUtc);
            if (sendNumber >= options.FastRetryCount
                || fastRetryDelay is null)
            {
                logger.LogWarning(
                    "Mock ERP operation {Operation} failed transiently with code {ErrorCode} for correlation {CorrelationId}.",
                    mapped.Operation,
                    result.ErrorCode,
                    step.CorrelationId);
                return result;
            }

            await Task.Delay(
                fastRetryDelay.Value,
                timeProvider,
                cancellationToken);
        }
    }

    private HttpRequestMessage CreateRequest(
        ClaimedIntegrationStep step,
        string path,
        byte[] payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(
            ApiKeyHeaderName,
            options.ApiKey);
        request.Headers.TryAddWithoutValidation(
            IdempotencyHeaderName,
            step.IdempotencyKey);
        request.Headers.TryAddWithoutValidation(
            CorrelationHeaderName,
            step.CorrelationId.ToString("D"));
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        return request;
    }

    private async Task<StepExecutionResult> ReadResultAsync(
        IntegrationStepType stepType,
        MockErpMappedRequest mapped,
        string requestHash,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        var statusCode = (short)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            try
            {
                return Success(
                    stepType,
                    statusCode,
                    requestHash,
                    mapped.MaskedPayload,
                    responseBody);
            }
            catch (JsonException)
            {
                return TransientFailure(
                    requestHash,
                    mapped.MaskedPayload,
                    "MockErp.InvalidResponse",
                    "The Mock ERP response was invalid.",
                    statusCode);
            }
        }

        MockErpErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<MockErpErrorResponse>(
                responseBody,
                JsonOptions);
        }
        catch (JsonException)
        {
            // The response is treated using its HTTP status and is never logged.
        }

        var retryAfterUtc = ReadRetryAfter(response);
        var retryable = IsRetryable(response.StatusCode, error);
        var errorCode = SafeCode(
            error?.Code,
            retryable
                ? "MockErp.TransientFailure"
                : "MockErp.PermanentFailure");
        var maskedResponse = JsonSerializer.Serialize(
            new
            {
                code = errorCode,
                retryable
            },
            JsonOptions);
        return retryable
            ? TransientFailure(
                requestHash,
                mapped.MaskedPayload,
                errorCode,
                "Mock ERP temporarily could not process the request.",
                statusCode,
                maskedResponse,
                retryAfterUtc)
            : new StepExecutionResult(
                IntegrationResultType.PermanentFailure,
                statusCode,
                requestHash,
                mapped.MaskedPayload,
                maskedResponse,
                errorCode,
                "Mock ERP rejected the request.",
                null,
                null);
    }

    private static StepExecutionResult Success(
        IntegrationStepType stepType,
        short statusCode,
        string requestHash,
        string maskedRequest,
        string responseBody)
    {
        string externalReference;
        object maskedResponse;
        switch (stepType)
        {
            case IntegrationStepType.EnsureCustomer:
                var customer = Deserialize<MockErpEnsureCustomerResponse>(
                    responseBody);
                externalReference = customer.ErpCustomerCode;
                maskedResponse = new
                {
                    customer.ErpCustomerId,
                    customer.ErpCustomerCode,
                    customer.ExternalCustomerId,
                    customer.Created
                };
                break;

            case IntegrationStepType.CreateOrder:
                var order = Deserialize<MockErpCreateOrderResponse>(
                    responseBody);
                externalReference = order.ErpOrderNumber;
                maskedResponse = order;
                break;

            case IntegrationStepType.CreateStockMovement:
                var stock = Deserialize<MockErpCreateStockMovementsResponse>(
                    responseBody);
                externalReference = stock.ErpOrderId.ToString("D");
                maskedResponse = new
                {
                    stock.ErpOrderId,
                    stock.ExternalOrderId,
                    movementCount = stock.Movements.Count
                };
                break;

            case IntegrationStepType.CreateAccountingEntry:
                var accounting =
                    Deserialize<MockErpCreateAccountingEntryResponse>(
                        responseBody);
                externalReference = accounting.ErpVoucherNumber;
                maskedResponse = accounting;
                break;

            default:
                throw new JsonException(
                    "The Mock ERP response type is unsupported.");
        }

        return new StepExecutionResult(
            IntegrationResultType.Succeeded,
            statusCode,
            requestHash,
            maskedRequest,
            JsonSerializer.Serialize(maskedResponse, JsonOptions),
            null,
            null,
            SafeExternalReference(externalReference),
            null);
    }

    private static T Deserialize<T>(string responseBody)
    {
        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new JsonException(
                "The Mock ERP response body was empty.");
    }

    private static bool IsRetryable(
        HttpStatusCode statusCode,
        MockErpErrorResponse? error)
    {
        if (statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (error?.Code.StartsWith(
                "Authentication.",
                StringComparison.Ordinal) == true
            || error?.Code.StartsWith(
                "Validation.",
                StringComparison.Ordinal) == true
            || string.Equals(
                error?.Code,
                "Idempotency.PayloadConflict",
                StringComparison.Ordinal))
        {
            return false;
        }

        return statusCode is HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
            && error?.Retryable is not false;
    }

    private DateTime? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return GetUtcNow().Add(delta);
        }

        return retryAfter?.Date?.UtcDateTime;
    }

    private TimeSpan? GetFastRetryDelay(
        int sendNumber,
        DateTime? retryAfterUtc)
    {
        if (retryAfterUtc.HasValue)
        {
            var retryAfterDelay = retryAfterUtc.Value - GetUtcNow();
            if (retryAfterDelay <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return retryAfterDelay <= options.MaxFastRetryDelay
                ? retryAfterDelay
                : null;
        }

        var multiplier = Math.Pow(2, Math.Min(sendNumber, 10));
        var milliseconds = Math.Min(
            options.MaxFastRetryDelay.TotalMilliseconds,
            options.FastRetryDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static StepExecutionResult TransientFailure(
        string requestHash,
        string maskedRequest,
        string errorCode,
        string errorMessage,
        short? statusCode = null,
        string? maskedResponse = null,
        DateTime? retryAfterUtc = null)
    {
        return new StepExecutionResult(
            IntegrationResultType.TransientFailure,
            statusCode,
            requestHash,
            maskedRequest,
            maskedResponse,
            SafeCode(errorCode, "MockErp.TransientFailure"),
            errorMessage,
            null,
            retryAfterUtc);
    }

    private static string SafeCode(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }

    private static string SafeExternalReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException(
                "The Mock ERP response omitted its external reference.");
        }

        return value.Length <= 100 ? value : value[..100];
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }
}
