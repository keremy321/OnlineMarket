using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Recommendation.Api.Contracts;

namespace Recommendation.Api.Infrastructure.Security;

public static class ApiKeyDefaults
{
    public const string Scheme = "RecommendationApiKey";
    public const string HeaderName = "X-Api-Key";
    internal const string FailureCodeItem =
        "Recommendation.ApiKeyFailureCode";
}

public sealed class ApiKeyAuthenticationOptions
    : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(
        options,
        logger,
        encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            Context.Items[ApiKeyDefaults.FailureCodeItem] =
                "Authentication.ConfigurationMissing";
            return Task.FromResult(AuthenticateResult.Fail(
                "Recommendation API-key authentication is not configured."));
        }

        if (!Request.Headers.TryGetValue(
                ApiKeyDefaults.HeaderName,
                out var suppliedValues)
            || suppliedValues.Count != 1
            || string.IsNullOrEmpty(suppliedValues[0]))
        {
            Context.Items[ApiKeyDefaults.FailureCodeItem] =
                "Authentication.ApiKeyRequired";
            return Task.FromResult(AuthenticateResult.Fail(
                "An API key is required."));
        }

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedValues[0]!));
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(Options.ApiKey));
        if (!CryptographicOperations.FixedTimeEquals(
                suppliedHash,
                expectedHash))
        {
            Context.Items[ApiKeyDefaults.FailureCodeItem] =
                "Authentication.ApiKeyInvalid";
            return Task.FromResult(AuthenticateResult.Fail(
                "The API key is invalid."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(
                ClaimTypes.NameIdentifier,
                "OnlineMarketOutboxClient")],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name)));
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        var code = Context.Items.TryGetValue(
            ApiKeyDefaults.FailureCodeItem,
            out var value)
                ? value as string
                : null;
        var configurationMissing = code
            == "Authentication.ConfigurationMissing";
        Response.StatusCode = configurationMissing
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ApiErrorResponse(
            code ?? "Authentication.ApiKeyRequired",
            configurationMissing
                ? "API-key authentication is unavailable."
                : "A valid API key is required.",
            configurationMissing));
    }
}
