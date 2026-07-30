using ErpIntegration.Api.Contracts;
using Microsoft.AspNetCore.Diagnostics;

namespace ErpIntegration.Api.Infrastructure.Http;

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
            "An unhandled ERP Integration API error occurred for {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode =
            StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "Integration.UnexpectedError",
                "An unexpected integration error occurred.",
                true),
            cancellationToken);
        return true;
    }
}
