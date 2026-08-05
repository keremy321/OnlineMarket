using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

/// <summary>
/// Administrative operations for the pseudonymous subject IDs derived from
/// customer IDs, used to keep purchase history pseudonymous in this service.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/recommendations/subjects")]
public sealed class RecommendationSubjectsController(
    IRecommendationSubjectBackfillService backfillService)
    : ControllerBase
{
    /// <summary>
    /// Scans stored interactions for records missing a derived subject ID and
    /// backfills them. Safe to run repeatedly: already-backfilled records are
    /// skipped rather than reprocessed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [Tags("Subject Management")]
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
