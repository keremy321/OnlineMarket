using Microsoft.AspNetCore.Diagnostics;
using Recommendation.Api.Contracts;

namespace Recommendation.Api.Infrastructure.Http;

public sealed class GlobalApiExceptionHandler(
    ILogger<GlobalApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "An unhandled Recommendation API error occurred for {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);
        httpContext.Response.StatusCode =
            StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "Recommendation.UnexpectedError",
                "An unexpected Recommendation API error occurred.",
                true),
            cancellationToken);
        return true;
    }
}
