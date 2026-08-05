using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Tests;

internal static class RecommendationModelEvaluationTestData
{
    public const string SubjectId =
        "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public static readonly Guid FirstProductId = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");

    public static readonly Guid SecondProductId = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");

    public static RecommendationModelEvaluationRequest Request(
        string version = "temporal-test-v1")
    {
        return new RecommendationModelEvaluationRequest(
            version,
            [FirstProductId, SecondProductId],
            [FirstProductId, SecondProductId],
            [Product(FirstProductId), Product(SecondProductId)],
            [
                new RecommendationModelEvaluationOrderRequest(
                    Guid.Parse("40000000-0000-0000-0000-000000000001"),
                    SubjectId,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    [
                        new RecommendationModelOrderInteractionItemRequest(
                            FirstProductId,
                            1)
                    ]),
                new RecommendationModelEvaluationOrderRequest(
                    Guid.Parse("40000000-0000-0000-0000-000000000002"),
                    SubjectId,
                    new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    [
                        new RecommendationModelOrderInteractionItemRequest(
                            SecondProductId,
                            1)
                    ])
            ]);
    }

    public static RecommendationModelEvaluationClientResponse Response(
        string version = "temporal-test-v1")
    {
        return new RecommendationModelEvaluationClientResponse(
            "Succeeded",
            version,
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            new string('a', 64),
            new RecommendationModelEvaluationDatasetResponse(2, 2, 1, 2, 2),
            new RecommendationModelEvaluationSplitResponse(
                "per_subject_chronological_newest_order_holdout",
                "Newest order held out after stable chronological sorting.",
                5,
                2,
                1,
                true,
                42,
                1,
                0,
                1,
                1,
                1,
                1),
            new RecommendationModelEvaluationExcludedDataResponse(
                0,
                0,
                0,
                0,
                0,
                0,
                0),
            new RecommendationModelEvaluationModelsResponse(
                EvaluatedModel(als: false),
                EvaluatedModel(als: true),
                EvaluatedModel(als: true, hybrid: true),
                NotEvaluatedModel(
                    "TF-IDF requires an item-to-item protocol."),
                NotEvaluatedModel(
                    "FBT requires a basket-item protocol.")),
            version,
            new RecommendationModelEvaluationReportFilesResponse(
                $"evaluation-{version}.json",
                $"evaluation-{version}.md"),
            ["Timing values are runtime-dependent."],
            new RecommendationModelEvaluationComparisonResponse(
                0m,
                0m,
                0m,
                0m,
                0m));
    }

    private static RecommendationModelEvaluationModelResponse EvaluatedModel(
        bool als,
        bool hybrid = false)
    {
        return new RecommendationModelEvaluationModelResponse(
            "Evaluated",
            null,
            new RecommendationModelEvaluationMetricsResponse(
                0.2m,
                1m,
                1m,
                1m,
                0.5m,
                0.5m,
                0.5m,
                1,
                1,
                1,
                10m,
                0.1m,
                0.2m,
                0),
            new RecommendationModelEvaluationParametersResponse(
                als ? "implicit_weight" : "total_quantity",
                true,
                als ? 32 : null,
                als ? 0.05m : null,
                als ? 20 : null,
                als ? 20m : null,
                als ? 42 : null,
                hybrid ? HybridParameters() : null));
    }

    private static RecommendationModelEvaluationModelResponse NotEvaluatedModel(
        string reason)
    {
        return new RecommendationModelEvaluationModelResponse(
            "NotEvaluated",
            reason,
            null,
            null);
    }

    public static RecommendationModelProductRequest Product(Guid productId)
    {
        return new RecommendationModelProductRequest(
            productId,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            $"Product {productId:N}",
            "Evaluation product",
            "Piece",
            1m,
            10m,
            true,
            true);
    }

    public static RecommendationModelHybridParametersResponse HybridParameters()
    {
        return new RecommendationModelHybridParametersResponse(
            new RecommendationModelPersonalizedHybridWeightsResponse(
                0.50m,
                0.20m,
                0.15m,
                0.15m),
            new RecommendationModelSimilarHybridWeightsResponse(
                0.70m,
                0.20m,
                0.10m),
            10,
            500,
            "maximum_similarity",
            "fallback_to_als_without_renormalization");
    }
}
