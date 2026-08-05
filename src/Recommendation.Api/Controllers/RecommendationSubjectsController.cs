using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/recommendations/subjects")]
public sealed class RecommendationSubjectsController(
    IRecommendationSubjectBackfillService backfillService)
    : ControllerBase
{
    [HttpPost("backfill")]
    [ProducesResponseType<RecommendationSubjectBackfillResponse>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<RecommendationSubjectBackfillResponse>>
        Backfill(CancellationToken cancellationToken)
    {
        var result = await backfillService.BackfillAsync(cancellationToken);
        return Ok(new RecommendationSubjectBackfillResponse(
            result.ScannedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.FailureCount));
    }
}
