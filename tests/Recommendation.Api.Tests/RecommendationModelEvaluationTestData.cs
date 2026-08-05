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
                NotEvaluatedModel(
                    "TF-IDF requires an item-to-item protocol."),
                NotEvaluatedModel(
                    "FBT requires a basket-item protocol.")),
            version,
            new RecommendationModelEvaluationReportFilesResponse(
                $"evaluation-{version}.json",
                $"evaluation-{version}.md"),
            ["Timing values are runtime-dependent."]);
    }

    private static RecommendationModelEvaluationModelResponse EvaluatedModel(
        bool als)
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
                als ? 42 : null));
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
}
