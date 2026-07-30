using Microsoft.AspNetCore.Diagnostics;
using MockErp.Api.Contracts;

namespace MockErp.Api.Infrastructure.Http;

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
            "An unhandled Mock ERP API error occurred for {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);
        httpContext.Response.StatusCode =
            StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "MockErp.UnexpectedError",
                "An unexpected Mock ERP error occurred.",
                true),
            cancellationToken);
        return true;
    }
}
