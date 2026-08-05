using Microsoft.Extensions.Options;

namespace OnlineMarket.Web.Infrastructure.Http;

public sealed class RecommendationApiKeyHandler(
    IOptions<RecommendationOutboxOptions> options)
    : DelegatingHandler
{
    private const string RecommendationQueryPathPrefix = "/api/v1/recommendations/";
    private static readonly HashSet<string> AuthenticatedEventPaths = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/events/products",
        "/api/v1/events/orders"
    };

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
                "is required when Recommendation service requests are enabled.");
        }

        request.Headers.Remove(RecommendationOutboxOptions.ApiKeyHeaderName);
        request.Headers.TryAddWithoutValidation(
            RecommendationOutboxOptions.ApiKeyHeaderName,
            apiKey);

        return base.SendAsync(request, cancellationToken);
    }

    private bool RequiresRecommendationAuthentication(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return false;
        }

        if (requestUri.IsAbsoluteUri
            && !IsConfiguredRecommendationApi(requestUri))
        {
            return false;
        }

        var path = requestUri.IsAbsoluteUri
            ? requestUri.AbsolutePath
            : requestUri.OriginalString.Split('?', 2)[0];
        return AuthenticatedEventPaths.Contains(path)
            || path.StartsWith(
                RecommendationQueryPathPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool IsConfiguredRecommendationApi(Uri requestUri)
    {
        if (!Uri.TryCreate(
                options.Value.RecommendationApiBaseAddress,
                UriKind.Absolute,
                out var recommendationApiUri))
        {
            return false;
        }

        return string.Equals(
                requestUri.Scheme,
                recommendationApiUri.Scheme,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                requestUri.Host,
                recommendationApiUri.Host,
                StringComparison.OrdinalIgnoreCase)
            && requestUri.Port == recommendationApiUri.Port;
    }
}
