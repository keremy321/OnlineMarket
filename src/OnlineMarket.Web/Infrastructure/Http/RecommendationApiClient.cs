using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Infrastructure.Http;

public class RecommendationApiClient : IRecommendationClient
{
    private const int SimilarProductLimit = 4;
    private const int PersonalizedProductLimit = 8;

    private readonly HttpClient _httpClient;
    private readonly ILogger<RecommendationApiClient> _logger;

    /// <summary>
    /// Shared read-timeout budget for Similar, Personalized and FrequentlyBoughtTogether.
    /// Sourced from <see cref="RecommendationApiClientOptions"/>; falls back to that
    /// option's default when none is supplied (existing callers that construct this
    /// client directly, e.g. in tests, are unaffected).
    /// </summary>
    private readonly TimeSpan _readTimeout;

    private static DateTime _offlineUntilUtc = DateTime.MinValue;
    private static readonly object _lock = new();

    public RecommendationApiClient(HttpClient httpClient, ILogger<RecommendationApiClient> logger)
        : this(httpClient, logger, options: null)
    {
    }

    public RecommendationApiClient(
        HttpClient httpClient,
        ILogger<RecommendationApiClient> logger,
        IOptions<RecommendationApiClientOptions>? options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _readTimeout = options?.Value.EffectiveReadTimeout
            ?? TimeSpan.FromSeconds(RecommendationApiClientOptions.DefaultReadTimeoutSeconds);
    }

    private bool IsCircuitOpen()
    {
        lock (_lock)
        {
            return DateTime.UtcNow < _offlineUntilUtc;
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            _offlineUntilUtc = DateTime.UtcNow.AddSeconds(15);
        }
    }

    public async Task<List<RecommendationItemDto>> GetPopularRecommendationsAsync(int count = 5)
    {
        if (IsCircuitOpen()) return new List<RecommendationItemDto>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetFromJsonAsync<List<RecommendationItemDto>>($"/api/v1/recommendations/popular?limit={count}", cts.Token);
            return response ?? new List<RecommendationItemDto>();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning("Recommendation.Api unavailable ({Message}). Circuit opened for 15s.", ex.Message);
            return new List<RecommendationItemDto>();
        }
    }

    public async Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(
        Guid productId,
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        if (IsCircuitOpen()) return new List<RecommendationItemDto>();

        // Linking preserves caller cancellation (e.g. the shopper closing the chat
        // panel) while still enforcing the read-timeout budget independently.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_readTimeout);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<RecommendationItemDto>>(
                $"/api/v1/recommendations/fbt/{productId}?limit={count}",
                timeoutCts.Token);
            return response ?? new List<RecommendationItemDto>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller canceled the request; this is not a Recommendation.Api failure,
            // so it must not open the circuit, log a warning, or be retried.
            throw;
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning("Recommendation.Api unavailable ({Message}). Circuit opened for 15s.", ex.Message);
            return new List<RecommendationItemDto>();
        }
    }

    public async Task<List<RecommendationItemDto>> GetSimilarProductsAsync(Guid productId, int count = 5)
    {
        if (productId == Guid.Empty || count <= 0)
        {
            return [];
        }

        var limit = Math.Min(count, SimilarProductLimit);
        try
        {
            using var cts = new CancellationTokenSource(_readTimeout);
            using var response = await _httpClient.GetAsync(
                $"/api/v1/recommendations/similar/{productId:D}?limit={limit}",
                cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                RecordFailure();
                _logger.LogWarning(
                    "Similar recommendations returned HTTP {StatusCode} for ProductId {ProductId} and Limit {Limit}.",
                    (int)response.StatusCode,
                    productId,
                    limit);
                return [];
            }

            var recommendations = await response.Content
                .ReadFromJsonAsync<List<SimilarRecommendationResponseDto>>(
                    cancellationToken: cts.Token);
            if (recommendations is null)
            {
                return [];
            }

            if (recommendations.Any(item =>
                    item.ProductId == Guid.Empty
                    || string.IsNullOrWhiteSpace(item.RecommendationType)
                    || string.IsNullOrWhiteSpace(item.ReasonCode)
                    || string.IsNullOrWhiteSpace(item.ReasonText)))
            {
                RecordFailure();
                _logger.LogWarning(
                    "Similar recommendations returned an invalid contract for ProductId {ProductId} and Limit {Limit}.",
                    productId,
                    limit);
                return [];
            }

            return recommendations
                .Take(limit)
                .Select(item => new RecommendationItemDto(
                    item.ProductId,
                    item.ReasonText,
                    item.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning(
                ex,
                "Similar recommendations were unavailable for ProductId {ProductId} and Limit {Limit}; the section will be hidden.",
                productId,
                limit);
            return [];
        }
    }

    public async Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(Guid customerId, int count = 5)
    {
        if (customerId == Guid.Empty || count <= 0 || IsCircuitOpen())
        {
            return [];
        }

        var limit = Math.Min(count, PersonalizedProductLimit);
        try
        {
            using var cts = new CancellationTokenSource(_readTimeout);
            using var response = await _httpClient.GetAsync(
                $"/api/v1/recommendations/customers/{customerId:D}?limit={limit}",
                cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                RecordFailure();
                _logger.LogWarning(
                    "Personalized recommendations returned a non-success response; the section will be hidden.");
                return [];
            }

            var recommendations = await response.Content
                .ReadFromJsonAsync<List<PersonalizedRecommendationResponseDto>>(
                    cancellationToken: cts.Token);
            if (recommendations is null)
            {
                return [];
            }

            if (recommendations.Any(item =>
                    item.ProductId == Guid.Empty
                    || !string.Equals(
                        item.RecommendationType,
                        "Personalized",
                        StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.ReasonCode)
                    || string.IsNullOrWhiteSpace(item.ReasonText)
                    || item.Metrics is null
                    || string.IsNullOrWhiteSpace(item.Metrics.RankingSource)))
            {
                RecordFailure();
                _logger.LogWarning(
                    "Personalized recommendations returned an invalid contract; the section will be hidden.");
                return [];
            }

            return recommendations
                .Take(limit)
                .Select(item => new RecommendationItemDto(
                    item.ProductId,
                    item.ReasonText,
                    item.Score,
                    RecommendationType: item.RecommendationType,
                    ReasonCode: item.ReasonCode,
                    PersonalizedMetrics: item.Metrics))
                .ToList();
        }
        catch (Exception)
        {
            RecordFailure();
            _logger.LogWarning(
                "Personalized recommendations were unavailable; the section will be hidden.");
            return [];
        }
    }

    public async Task<List<RecommendationItemDto>> GetCartCompletionRecommendationsAsync(List<Guid> productIds, int count = 5)
    {
        if (productIds.Count == 0 || count <= 0)
        {
            return [];
        }

        var distinctProductIds = productIds.Distinct().ToArray();
        try
        {
            if (IsCircuitOpen())
            {
                return [];
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/recommendations/cart",
                new
                {
                    ProductIds = distinctProductIds,
                    Limit = count
                },
                cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<RecommendationItemDto>>(cancellationToken: cts.Token);
                if (result is { Count: > 0 })
                {
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Cart-completion recommendations were unavailable ({Message}); using popularity fallback.",
                ex.Message);
        }

        var popular = await GetPopularRecommendationsAsync(
            Math.Min(100, count + distinctProductIds.Length));
        var cartProductIds = distinctProductIds.ToHashSet();
        return popular
            .Where(item => !cartProductIds.Contains(item.ProductId))
            .DistinctBy(item => item.ProductId)
            .Take(count)
            .ToList();
    }
}
