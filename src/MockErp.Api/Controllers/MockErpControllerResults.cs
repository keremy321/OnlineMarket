using Microsoft.AspNetCore.Mvc;
using MockErp.Api.Application.Models;
using MockErp.Api.Contracts;

namespace MockErp.Api.Controllers;

internal static class MockErpControllerResults
{
    public static IActionResult FromOperation(OperationResult result)
    {
        if (result.ValidationErrors is not null)
        {
            return new BadRequestObjectResult(
                new ApiValidationErrorResponse(
                    "Validation.Failed",
                    "Request validation failed.",
                    false,
                    result.ValidationErrors));
        }

        if (result.Error is not null)
        {
            return new ObjectResult(new ApiErrorResponse(
                result.Error.Code,
                result.Error.Message,
                result.Error.Retryable))
            {
                StatusCode = result.Error.StatusCode
            };
        }

        var response = result.Response
            ?? throw new InvalidOperationException(
                "A successful operation did not contain a response.");
        return new ContentResult
        {
            StatusCode = response.StatusCode,
            ContentType = "application/json; charset=utf-8",
            Content = response.Body
        };
    }
}
