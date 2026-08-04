using System.Net.Http.Json;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Infrastructure.Http;

public class RecommendationApiClient : IRecommendationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecommendationApiClient> _logger;

    private static DateTime _offlineUntilUtc = DateTime.MinValue;
    private static readonly object _lock = new();

    public RecommendationApiClient(HttpClient httpClient, ILogger<RecommendationApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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

    public async Task<List<RecommendationItemDto>> GetFrequentlyBoughtTogetherAsync(Guid productId, int count = 5)
    {
        if (IsCircuitOpen()) return new List<RecommendationItemDto>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetFromJsonAsync<List<RecommendationItemDto>>($"/api/v1/recommendations/fbt/{productId}?limit={count}", cts.Token);
            return response ?? new List<RecommendationItemDto>();
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
        if (IsCircuitOpen()) return new List<RecommendationItemDto>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetFromJsonAsync<List<RecommendationItemDto>>($"/api/v1/recommendations/products/{productId}/similar?count={count}", cts.Token);
            return response ?? new List<RecommendationItemDto>();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning("Recommendation.Api unavailable ({Message}). Circuit opened for 15s.", ex.Message);
            return new List<RecommendationItemDto>();
        }
    }

    public async Task<List<RecommendationItemDto>> GetPersonalizedRecommendationsAsync(Guid customerId, int count = 5)
    {
        if (IsCircuitOpen()) return new List<RecommendationItemDto>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetFromJsonAsync<List<RecommendationItemDto>>($"/api/v1/recommendations/customers/{customerId}?count={count}", cts.Token);
            return response ?? new List<RecommendationItemDto>();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning("Recommendation.Api unavailable ({Message}). Circuit opened for 15s.", ex.Message);
            return new List<RecommendationItemDto>();
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
