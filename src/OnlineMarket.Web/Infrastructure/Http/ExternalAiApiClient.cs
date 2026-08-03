using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Infrastructure.Http;

public class ExternalAiApiClient : IAiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AiAssistantOptions> _options;
    private readonly ILogger<ExternalAiApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ExternalAiApiClient(
        HttpClient httpClient,
        IOptions<AiAssistantOptions> options,
        ILogger<ExternalAiApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<AiApiCompletionResponse?> GenerateCompletionAsync(
        AiApiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var config = _options.Value;

        if (!config.Enabled || string.IsNullOrWhiteSpace(config.EndpointUrl))
        {
            _logger.LogDebug("External AI API call skipped: Provider or EndpointUrl not configured.");
            return null;
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.EndpointUrl);

            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                httpRequest.Headers.TryAddWithoutValidation("api-key", config.ApiKey);
            }

            var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "External AI API endpoint returned non-success status code {StatusCode}: {ErrorBody}",
                    response.StatusCode,
                    errorBody);
                return null;
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<AiApiCompletionResponse>(
                responseStream,
                JsonOptions,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to communicate with external AI API endpoint: {Endpoint}", config.EndpointUrl);
            return null;
        }
    }
}
