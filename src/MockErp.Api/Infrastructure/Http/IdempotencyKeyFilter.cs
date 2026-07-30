using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MockErp.Api.Contracts;

namespace MockErp.Api.Infrastructure.Http;

public sealed class IdempotencyKeyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const string ContextItemName = "MockErp.IdempotencyKey";

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(
                HeaderName,
                out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]))
        {
            context.Result = new BadRequestObjectResult(
                new ApiErrorResponse(
                    "Idempotency.KeyRequired",
                    "A single non-empty Idempotency-Key header is required.",
                    false));
            return;
        }

        var key = values[0]!.Trim();
        if (key.Length > 200)
        {
            context.Result = new BadRequestObjectResult(
                new ApiErrorResponse(
                    "Validation.Failed",
                    "Idempotency-Key must not exceed 200 characters.",
                    false));
            return;
        }

        context.HttpContext.Items[ContextItemName] = key;
        await next();
    }

    public static string GetRequiredKey(HttpContext context)
    {
        return context.Items.TryGetValue(ContextItemName, out var value)
            && value is string key
                ? key
                : throw new InvalidOperationException(
                    "The idempotency-key filter did not supply a key.");
    }
}
