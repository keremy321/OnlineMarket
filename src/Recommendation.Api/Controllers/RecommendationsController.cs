using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Contracts;
using Recommendation.Api.Domain.Enums;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[Route("api/v1/recommendations")]
public sealed class RecommendationsController(
    IPopularityRecommendationService popularityService,
    IFrequentlyBoughtTogetherRecommendationService fbtService,
    ICartCompletionRecommendationService cartCompletionService,
    ISimilarRecommendationService similarService,
    IPersonalizedRecommendationService personalizedService,
    IRecommendationModelOrchestrationService modelOrchestrationService,
    IRecommendationModelEvaluationService modelEvaluationService)
    : ControllerBase
{
    [HttpGet("popular")]
    [ProducesResponseType<IReadOnlyList<RecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecommendationResponse>>>
        GetPopular(
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await popularityService.GetPopularAsync(
            limit,
            cancellationToken);
        return Ok(items.Select(MapRecommendation).ToArray());
    }

    [HttpGet("customers/{customerId:guid}")]
    [ProducesResponseType<IReadOnlyList<PersonalizedRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<PersonalizedRecommendationResponse>>> GetPersonalized(
            Guid customerId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            [FromQuery(Name = "excludePreviouslyPurchased")]
            bool excludePreviouslyPurchased = true,
            CancellationToken cancellationToken = default)
    {
        var items = await personalizedService.GetAsync(
            customerId,
            limit,
            excludePreviouslyPurchased,
            cancellationToken);
        return Ok(items.Select(MapPersonalizedRecommendation).ToArray());
    }

    [HttpGet("fbt/{productId:guid}")]
    [ProducesResponseType<
        IReadOnlyList<FrequentlyBoughtTogetherRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<FrequentlyBoughtTogetherRecommendationResponse>>> GetFbt(
            Guid productId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await fbtService.GetAsync(
            productId,
            limit,
            cancellationToken);
        return Ok(items.Select(MapFbtRecommendation).ToArray());
    }

    [HttpPost("cart")]
    [ProducesResponseType<
        IReadOnlyList<CartCompletionRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<
        IReadOnlyList<CartCompletionRecommendationResponse>>> GetCartCompletion(
            [FromBody] CartCompletionRecommendationRequest request,
            CancellationToken cancellationToken)
    {
        var items = await cartCompletionService.GetAsync(
            request.ProductIds!,
            request.Limit,
            cancellationToken);
        return Ok(items.Select(MapCartCompletionRecommendation).ToArray());
    }

    [HttpGet("similar/{productId:guid}")]
    [ProducesResponseType<IReadOnlyList<SimilarRecommendationResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SimilarRecommendationResponse>>>
        GetSimilar(
            Guid productId,
            [FromQuery(Name = "limit"), Range(1, int.MaxValue)] int? limit,
            CancellationToken cancellationToken)
    {
        var items = await similarService.GetAsync(
            productId,
            limit,
            cancellationToken);
        return Ok(items.Select(MapSimilarRecommendation).ToArray());
    }

    [HttpPost("recalculate-models")]
    [ProducesResponseType<RecommendationModelRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RecalculateModels(
        CancellationToken cancellationToken)
    {
        var result = await modelOrchestrationService.RecalculateAsync(
            cancellationToken);
        if (result.Outcome != RecommendationModelClientOutcome.Succeeded
            || result.Metadata is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    "RecommendationModel.Unavailable",
                    "Recommendation model training is currently unavailable.",
                    true));
        }

        return Ok(new RecommendationModelRecalculationResponse(
            result.Metadata.ModelVersion,
            "Succeeded",
            result.Metadata.TrainedAtUtc,
            result.Metadata.ProductCount,
            result.Metadata.SubjectCount,
            result.Metadata.InteractionCount,
            result.Metadata.InputHash,
            result.Metadata.Algorithm,
            result.Metadata.AlgorithmComponents,
            new RecommendationModelComponentStatusesContract(
                new RecommendationModelComponentStatusContract(
                    result.Metadata.Components.Tfidf.Status,
                    result.Metadata.Components.Tfidf
                        .TrainingDurationMilliseconds),
                new RecommendationModelComponentStatusContract(
                    result.Metadata.Components.Als.Status,
                    result.Metadata.Components.Als
                        .TrainingDurationMilliseconds),
                MapComponentStatus(result.Metadata.Components.Popularity),
                MapComponentStatus(result.Metadata.Components.Association),
                MapComponentStatus(result.Metadata.Components.Hybrid)),
            new RecommendationModelAlsParametersContract(
                result.Metadata.AlsParameters.Factors,
                result.Metadata.AlsParameters.Regularization,
                result.Metadata.AlsParameters.Iterations,
                result.Metadata.AlsParameters.Alpha,
                result.Metadata.AlsParameters.RandomSeed),
            MapHybridParameters(result.Metadata.HybridParameters)));
    }

    [HttpPost("evaluate-models")]
    [ProducesResponseType<RecommendationModelEvaluationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> EvaluateModels(
        CancellationToken cancellationToken)
    {
        var result = await modelEvaluationService.EvaluateAsync(
            cancellationToken);
        if (result.Outcome != RecommendationModelClientOutcome.Succeeded
            || result.Value is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    "RecommendationModel.EvaluationUnavailable",
                    "Recommendation model evaluation is currently unavailable.",
                    true));
        }

        return Ok(MapModelEvaluation(result.Value));
    }

    [HttpPost("recalculate")]
    [ProducesResponseType<RecommendationRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recalculate(
        CancellationToken cancellationToken)
    {
        var result = await popularityService.RecalculateAsync(
            cancellationToken);
        if (result.Outcome
            == PopularityRecalculationOutcome.AlreadyInProgress)
        {
            return Conflict(new ApiErrorResponse(
                "Recommendation.RunAlreadyInProgress",
                "A recommendation recalculation is already in progress.",
                false));
        }

        return Ok(new RecommendationRecalculationResponse(
            result.RunId
                ?? throw new InvalidOperationException(
                    "A successful recalculation did not return a run ID."),
            "Succeeded",
            result.OutputRecordCount));
    }

    [HttpPost("recalculate-fbt")]
    [ProducesResponseType<RecommendationRecalculationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecalculateFbt(
        CancellationToken cancellationToken)
    {
        var result = await fbtService.RecalculateAsync(cancellationToken);
        if (result.Outcome == FbtRecalculationOutcome.AlreadyInProgress)
        {
            return Conflict(new ApiErrorResponse(
                "Recommendation.RunAlreadyInProgress",
                "A recommendation recalculation is already in progress.",
                false));
        }

        return Ok(new RecommendationRecalculationResponse(
            result.RunId
                ?? throw new InvalidOperationException(
                    "A successful recalculation did not return a run ID."),
            "Succeeded",
            result.OutputRecordCount));
    }

    private static RecommendationResponse MapRecommendation(
        PopularityRecommendationItem item)
    {
        return new RecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Popular),
            "Popularity.RecentConfirmedOrders",
            "Popular based on recent confirmed orders.",
            new PopularityMetricsResponse(
                item.SoldQuantity,
                item.DistinctOrderCount,
                item.WindowStartUtc,
                item.WindowEndUtc));
    }

    private static FrequentlyBoughtTogetherRecommendationResponse
        MapFbtRecommendation(FbtRecommendationItem item)
    {
        return new FrequentlyBoughtTogetherRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.FrequentlyBoughtTogether),
            "Association.FrequentlyBoughtTogether",
            "Frequently purchased with this product.",
            new AssociationMetricsResponse(
                item.PairOrderCount,
                item.Support,
                item.Confidence,
                item.Lift));
    }

    private static CartCompletionRecommendationResponse
        MapCartCompletionRecommendation(CartCompletionRecommendationItem item)
    {
        return new CartCompletionRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.CartCompletion),
            "Association.CartCompletion",
            "Frequently purchased with products in the cart.",
            new CartCompletionMetricsResponse(
                item.SupportingCartProductCount,
                item.Confidence,
                item.Lift));
    }

    private static SimilarRecommendationResponse MapSimilarRecommendation(
        SimilarRecommendationItem item)
    {
        return new SimilarRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Similar),
            item.ReasonCode ?? (item.RankingSource
                == SimilarRankingSource.PythonTfidf
                    ? "Similarity.TfidfContent"
                    : "Similarity.DeterministicFallback"),
            item.ReasonText ?? (item.RankingSource
                == SimilarRankingSource.PythonTfidf
                    ? "Similar product based on content features."
                    : "Similar product based on local content attributes."),
            new SimilarityMetricsResponse(
                item.TfidfScore,
                item.PopularityScore,
                item.FrequentlyBoughtTogetherScore,
                item.FallbackScore,
                item.RankingSource.ToString(),
                item.ModelVersion,
                item.CoPurchaseScore));
    }

    private static PersonalizedRecommendationResponse
        MapPersonalizedRecommendation(PersonalizedRecommendationItem item)
    {
        var python = item.RankingSource is
            PersonalizedRankingSource.PythonImplicitAls
            or PersonalizedRankingSource.PythonHybrid;
        return new PersonalizedRecommendationResponse(
            item.ProductId,
            item.Score,
            nameof(RecommendationType.Personalized),
            item.ReasonCode ?? (python
                ? "Personalized.ImplicitAls"
                : "Personalized.PreferenceFallback"),
            item.ReasonText ?? (python
                ? "Recommended from pseudonymous purchase interactions."
                : "Recommended from purchase preferences and popularity."),
            new PersonalizedMetricsResponse(
                item.Confidence,
                item.RankingSource.ToString(),
                item.ModelVersion,
                item.AlsScore,
                item.ContentAffinityScore,
                item.AssociationScore,
                item.PopularityScore,
                item.FinalScore));
    }

    private static RecommendationModelEvaluationResponse MapModelEvaluation(
        RecommendationModelEvaluationClientResponse response)
    {
        return new RecommendationModelEvaluationResponse(
            response.Status,
            response.EvaluationVersion,
            response.EvaluatedAtUtc,
            response.InputHash,
            new RecommendationModelEvaluationDatasetContract(
                response.Dataset.ProductCount,
                response.Dataset.CandidateProductCount,
                response.Dataset.SubjectCount,
                response.Dataset.OrderCount,
                response.Dataset.InteractionCount),
            new RecommendationModelEvaluationSplitContract(
                response.Split.Strategy,
                response.Split.Description,
                response.Split.K,
                response.Split.MinimumHistoricalOrdersPerSubject,
                response.Split.HoldoutOrderCount,
                response.Split.ExcludePreviouslyPurchased,
                response.Split.RandomSeed,
                response.Split.EligibleSubjectCount,
                response.Split.ExcludedSubjectCount,
                response.Split.TrainingOrderCount,
                response.Split.TestOrderCount,
                response.Split.TrainingInteractionCount,
                response.Split.TestInteractionCount),
            new RecommendationModelEvaluationExcludedDataContract(
                response.ExcludedData.InsufficientHistorySubjectCount,
                response.ExcludedData.NoUsableTrainingHistorySubjectCount,
                response.ExcludedData.NoUsableTestInteractionsSubjectCount,
                response.ExcludedData.DevelopmentCapSubjectCount,
                response.ExcludedData.ProductsAbsentFromTrainingInteractions,
                response.ExcludedData.UnknownProductInteractionCount,
                response.ExcludedData.PopularityFallbackSubjectCount),
            new RecommendationModelEvaluationModelsContract(
                MapEvaluationModel(response.Models.Popularity),
                MapEvaluationModel(response.Models.Als),
                MapEvaluationModel(response.Models.Hybrid),
                MapEvaluationModel(response.Models.Tfidf),
                MapEvaluationModel(response.Models.Fbt)),
            response.Comparison is null
                ? null
                : new RecommendationModelEvaluationComparisonContract(
                    response.Comparison.HybridMinusAlsPrecisionAt5,
                    response.Comparison.HybridMinusAlsRecallAt5,
                    response.Comparison.HybridMinusAlsHitRateAt5,
                    response.Comparison.HybridMinusAlsNdcgAt5,
                    response.Comparison.HybridMinusAlsCoverage),
            response.ReportIdentifier,
            new RecommendationModelEvaluationReportFilesContract(
                response.Reports.JsonFile,
                response.Reports.MarkdownFile),
            response.Limitations);
    }

    private static RecommendationModelComponentStatusContract?
        MapComponentStatus(RecommendationModelComponentStatusResponse? status)
    {
        return status is null
            ? null
            : new RecommendationModelComponentStatusContract(
                status.Status,
                status.TrainingDurationMilliseconds);
    }

    private static RecommendationModelHybridParametersContract?
        MapHybridParameters(
            RecommendationModelHybridParametersResponse? parameters)
    {
        return parameters is null
            ? null
            : new RecommendationModelHybridParametersContract(
                new RecommendationModelPersonalizedHybridWeightsContract(
                    parameters.PersonalizedWeights.Als,
                    parameters.PersonalizedWeights.ContentAffinity,
                    parameters.PersonalizedWeights.Association,
                    parameters.PersonalizedWeights.Popularity),
                new RecommendationModelSimilarHybridWeightsContract(
                    parameters.SimilarWeights.ContentSimilarity,
                    parameters.SimilarWeights.CoPurchaseSimilarity,
                    parameters.SimilarWeights.Popularity),
                parameters.CandidatePoolMultiplier,
                parameters.CandidatePoolCap,
                parameters.ContentAffinityAggregation,
                parameters.MissingComponentPolicy);
    }

    private static RecommendationModelEvaluationModelContract
        MapEvaluationModel(
            RecommendationModelEvaluationModelResponse model)
    {
        return new RecommendationModelEvaluationModelContract(
            model.Status,
            model.Reason,
            model.Metrics is null
                ? null
                : new RecommendationModelEvaluationMetricsContract(
                    model.Metrics.PrecisionAtK,
                    model.Metrics.RecallAtK,
                    model.Metrics.HitRateAtK,
                    model.Metrics.NdcgAtK,
                    model.Metrics.CatalogueCoverage,
                    model.Metrics.KnownSubjectCatalogueCoverage,
                    model.Metrics.CatalogueCoverageIncludingFallback,
                    model.Metrics.EligibleSubjectCount,
                    model.Metrics.TrainingInteractionCount,
                    model.Metrics.TestInteractionCount,
                    model.Metrics.TrainingDurationMilliseconds,
                    model.Metrics.AverageInferenceLatencyMilliseconds,
                    model.Metrics.P95InferenceLatencyMilliseconds,
                    model.Metrics.FallbackSubjectCount),
            model.Parameters is null
                ? null
                : new RecommendationModelEvaluationParametersContract(
                    model.Parameters.InteractionWeighting,
                    model.Parameters.ExcludePreviouslyPurchased,
                    model.Parameters.Factors,
                    model.Parameters.Regularization,
                    model.Parameters.Iterations,
                    model.Parameters.Alpha,
                    model.Parameters.RandomSeed,
                    MapHybridParameters(model.Parameters.Hybrid)));
    }
}
