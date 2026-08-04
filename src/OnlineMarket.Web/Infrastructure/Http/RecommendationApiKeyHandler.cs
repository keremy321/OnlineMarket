using Microsoft.Extensions.Options;

namespace OnlineMarket.Web.Infrastructure.Http;

public sealed class RecommendationApiKeyHandler(
    IOptions<RecommendationOutboxOptions> options)
    : DelegatingHandler
{
    private static readonly HashSet<string> AuthenticatedPaths =
    [
        "/api/v1/events/products",
        "/api/v1/events/orders"
    ];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!RequiresRecommendationAuthentication(request.RequestUri))
        {
            return base.SendAsync(request, cancellationToken);
        }

        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Configuration value '{RecommendationOutboxOptions.SectionName}:ApiKey' " +
                "is required when Recommendation Outbox delivery is enabled.");
        }

        request.Headers.Remove(RecommendationOutboxOptions.ApiKeyHeaderName);
        request.Headers.TryAddWithoutValidation(
            RecommendationOutboxOptions.ApiKeyHeaderName,
            apiKey);

        return base.SendAsync(request, cancellationToken);
    }

    private static bool RequiresRecommendationAuthentication(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return false;
        }

        var path = requestUri.IsAbsoluteUri
            ? requestUri.AbsolutePath
            : requestUri.OriginalString.Split('?', 2)[0];
        return AuthenticatedPaths.Contains(path);
    }
}
