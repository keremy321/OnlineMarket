using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ErpIntegration.Api.Controllers;

[ApiController]
[Route("api/v1/integration/jobs")]
public sealed class IntegrationJobsController(
    IIntegrationOrderService orderService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<IntegrationJobSummaryResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] IntegrationBatchStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var errors = PaginationValidation.Validate(page, pageSize);
        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            errors[nameof(status)] =
            [
                "Status must be a supported IntegrationBatchStatus value."
            ];
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(errors));
        }

        return Ok(await orderService.GetJobsAsync(
            status,
            page,
            pageSize,
            cancellationToken));
    }
}

internal static class PaginationValidation
{
    public static Dictionary<string, string[]> Validate(
        int page,
        int pageSize)
    {
        var errors = new Dictionary<string, string[]>(
            StringComparer.Ordinal);
        if (page < 1)
        {
            errors[nameof(page)] = ["Page must be at least one."];
        }

        if (pageSize is < 1 or > 100)
        {
            errors[nameof(pageSize)] =
            [
                "PageSize must be between one and 100."
            ];
        }

        return errors;
    }
}
