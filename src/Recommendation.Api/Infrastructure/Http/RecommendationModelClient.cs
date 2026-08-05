using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Infrastructure.Http;

public sealed class RecommendationModelClient(
    HttpClient httpClient,
    IOptions<RecommendationModelServiceOptions> options,
    RecommendationModelCircuitBreaker circuitBreaker,
    ILogger<RecommendationModelClient> logger)
    : IRecommendationModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task<RecommendationModelClientResult<
        RecommendationModelTrainingClientResponse>> TrainAsync(
            RecommendationModelTrainingRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<
            RecommendationModelTrainingRequest,
            RecommendationModelTrainingClientResponse>(
                "/api/v1/models/train",
                request,
                response => IsValidTrainingResponse(request, response),
                options.Value.Timeout,
                participatesInInferenceCircuit: true,
                cancellationToken);
    }

    public Task<RecommendationModelClientResult<
        RecommendationModelSimilarClientResponse>> GetSimilarAsync(
            RecommendationModelSimilarRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<
            RecommendationModelSimilarRequest,
            RecommendationModelSimilarClientResponse>(
                "/api/v1/models/similar",
                request,
                response => IsValidSimilarResponse(request, response),
                options.Value.Timeout,
                participatesInInferenceCircuit: true,
                cancellationToken);
    }

    public Task<RecommendationModelClientResult<
        RecommendationModelPersonalizedClientResponse>> GetPersonalizedAsync(
            RecommendationModelPersonalizedRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<
            RecommendationModelPersonalizedRequest,
            RecommendationModelPersonalizedClientResponse>(
                "/api/v1/models/personalized",
                request,
                response => IsValidPersonalizedResponse(request, response),
                options.Value.Timeout,
                participatesInInferenceCircuit: true,
                cancellationToken);
    }

    public Task<RecommendationModelClientResult<
        RecommendationModelEvaluationClientResponse>> EvaluateAsync(
            RecommendationModelEvaluationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<
            RecommendationModelEvaluationRequest,
            RecommendationModelEvaluationClientResponse>(
                "/api/v1/models/evaluate",
                request,
                response => IsValidEvaluationResponse(request, response),
                options.Value.EvaluationTimeout,
                participatesInInferenceCircuit: false,
                cancellationToken);
    }

    private async Task<RecommendationModelClientResult<TResponse>> SendAsync<
        TRequest,
        TResponse>(
            string path,
            TRequest payload,
            Func<TResponse, bool> validate,
            TimeSpan timeout,
            bool participatesInInferenceCircuit,
            CancellationToken cancellationToken)
        where TResponse : class
    {
        if (participatesInInferenceCircuit && !circuitBreaker.TryEnter())
        {
            logger.LogInformation(
                "Recommendation model-service circuit is open for {Path}.",
                path);
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.Unavailable);
        }

        try
        {
            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.TryAddWithoutValidation(
                ApiKeyDefaults.HeaderName,
                options.Value.ApiKey);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (participatesInInferenceCircuit)
                {
                    circuitBreaker.RecordFailure();
                }
                logger.LogWarning(
                    "Recommendation model service returned status {StatusCode} for {Path}.",
                    (int)response.StatusCode,
                    path);
                return new RecommendationModelClientResult<TResponse>(
                    RecommendationModelClientOutcome.Unavailable);
            }

            var value = await response.Content.ReadFromJsonAsync<TResponse>(
                JsonOptions,
                timeoutSource.Token);
            if (value is null || !validate(value))
            {
                if (participatesInInferenceCircuit)
                {
                    circuitBreaker.RecordFailure();
                }
                logger.LogWarning(
                    "Recommendation model service returned an invalid response for {Path}.",
                    path);
                return new RecommendationModelClientResult<TResponse>(
                    RecommendationModelClientOutcome.InvalidResponse);
            }

            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordSuccess();
            }
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.Succeeded,
                value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordFailure();
            }
            logger.LogWarning(
                "Recommendation model service timed out for {Path}.",
                path);
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.Unavailable);
        }
        catch (OperationCanceledException)
        {
            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordCancellation();
            }
            throw;
        }
        catch (HttpRequestException exception)
        {
            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordFailure();
            }
            logger.LogWarning(
                exception,
                "Recommendation model service connection failed for {Path}.",
                path);
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.Unavailable);
        }
        catch (JsonException exception)
        {
            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordFailure();
            }
            logger.LogWarning(
                exception,
                "Recommendation model service returned malformed JSON for {Path}.",
                path);
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.InvalidResponse);
        }
        catch (NotSupportedException exception)
        {
            if (participatesInInferenceCircuit)
            {
                circuitBreaker.RecordFailure();
            }
            logger.LogWarning(
                exception,
                "Recommendation model service response contract was invalid for {Path}.",
                path);
            return new RecommendationModelClientResult<TResponse>(
                RecommendationModelClientOutcome.InvalidResponse);
        }
    }

    private static bool IsValidTrainingResponse(
        RecommendationModelTrainingRequest request,
        RecommendationModelTrainingClientResponse response)
    {
        return string.Equals(
                response.Status,
                "Succeeded",
                StringComparison.Ordinal)
            && response.Metadata is not null
            && response.Metadata.ModelVersion == request.ModelVersion
            && response.Metadata.CorrelationId == request.CorrelationId
            && response.Metadata.ProductCount == request.Products.Count
            && response.Metadata.SubjectCount
                == request.Interactions
                    .Select(interaction => interaction.SubjectId)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            && response.Metadata.InteractionCount
                == request.Interactions.Sum(interaction =>
                    interaction.Items.Count)
            && response.Metadata.TrainedAtUtc.Kind == DateTimeKind.Utc
            && response.Metadata.InputHash.Length == 64
            && !string.IsNullOrWhiteSpace(response.Metadata.Algorithm)
            && response.Metadata.AlgorithmComponents is not null
            && response.Metadata.AlgorithmComponents.Count > 0
            && response.Metadata.Components is not null
            && IsValidComponentStatus(response.Metadata.Components.Tfidf)
            && IsValidComponentStatus(response.Metadata.Components.Als)
            && response.Metadata.Components.Tfidf.Status == "Succeeded"
            && response.Metadata.AlsParameters is
            {
                Factors: > 0,
                Regularization: > 0m,
                Iterations: > 0,
                Alpha: > 0m,
                RandomSeed: >= 0
            }
            && response.Metadata.LibraryVersions is not null;
    }

    private static bool IsValidSimilarResponse(
        RecommendationModelSimilarRequest request,
        RecommendationModelSimilarClientResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.ModelVersion)
            || response.SourceProductId != request.ProductId
            || response.Items is null
            || response.Items.Count > request.Limit)
        {
            return false;
        }

        var productIds = new HashSet<Guid>();
        decimal? previousScore = null;
        Guid? previousProductId = null;
        foreach (var item in response.Items)
        {
            if (item.ProductId == Guid.Empty
                || item.ProductId == request.ProductId
                || item.TfidfScore is < 0m or > 1m
                || !productIds.Add(item.ProductId))
            {
                return false;
            }

            if (previousScore.HasValue
                && (item.TfidfScore > previousScore.Value
                    || item.TfidfScore == previousScore.Value
                    && item.ProductId.CompareTo(previousProductId!.Value) < 0))
            {
                return false;
            }

            previousScore = item.TfidfScore;
            previousProductId = item.ProductId;
        }

        return true;
    }

    private static bool IsValidPersonalizedResponse(
        RecommendationModelPersonalizedRequest request,
        RecommendationModelPersonalizedClientResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.ModelVersion)
            || response.Strategy is not (
                "implicit_als" or "cold_start_unavailable")
            || response.Recommendations is null
            || response.Recommendations.Count > request.Limit
            || response.Strategy == "cold_start_unavailable"
                && response.Recommendations.Count != 0)
        {
            return false;
        }

        var productIds = new HashSet<Guid>();
        decimal? previousScore = null;
        Guid? previousProductId = null;
        foreach (var item in response.Recommendations)
        {
            if (item.ProductId == Guid.Empty
                || item.Score is < 0m or > 1m
                || item.Confidence is < 0m or > 1m
                || string.IsNullOrWhiteSpace(item.ReasonCode)
                || string.IsNullOrWhiteSpace(item.ReasonText)
                || !productIds.Add(item.ProductId))
            {
                return false;
            }

            if (previousScore.HasValue
                && (item.Score > previousScore.Value
                    || item.Score == previousScore.Value
                    && item.ProductId.CompareTo(previousProductId!.Value) < 0))
            {
                return false;
            }

            previousScore = item.Score;
            previousProductId = item.ProductId;
        }

        return true;
    }

    private static bool IsValidEvaluationResponse(
        RecommendationModelEvaluationRequest request,
        RecommendationModelEvaluationClientResponse response)
    {
        if (response.Status != "Succeeded"
            || response.EvaluationVersion != request.EvaluationVersion
            || response.EvaluatedAtUtc.Kind != DateTimeKind.Utc
            || !IsSha256(response.InputHash)
            || response.Dataset is null
            || response.Split is null
            || response.ExcludedData is null
            || response.Models is null
            || response.Reports is null
            || response.Limitations is null
            || response.Dataset.ProductCount
                != request.CatalogueProductIds.Count
            || response.Dataset.CandidateProductCount
                != request.CandidateProductIds.Count
            || response.Dataset.SubjectCount
                != request.Interactions
                    .Select(interaction => interaction.SubjectId)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            || response.Dataset.OrderCount != request.Interactions.Count
            || response.Dataset.InteractionCount
                != request.Interactions.Sum(interaction =>
                    interaction.Items.Count)
            || response.Split.Strategy
                != "per_subject_chronological_newest_order_holdout"
            || string.IsNullOrWhiteSpace(response.Split.Description)
            || response.Split.K <= 0
            || response.Split.MinimumHistoricalOrdersPerSubject < 2
            || response.Split.HoldoutOrderCount <= 0
            || response.Split.RandomSeed < 0
            || response.Split.EligibleSubjectCount < 0
            || response.Split.ExcludedSubjectCount < 0
            || response.Split.EligibleSubjectCount
                + response.Split.ExcludedSubjectCount
                != response.Dataset.SubjectCount
            || response.Split.TrainingOrderCount < 0
            || response.Split.TestOrderCount < 0
            || response.Split.TrainingInteractionCount < 0
            || response.Split.TestInteractionCount < 0
            || response.ExcludedData.InsufficientHistorySubjectCount < 0
            || response.ExcludedData.NoUsableTrainingHistorySubjectCount < 0
            || response.ExcludedData.NoUsableTestInteractionsSubjectCount < 0
            || response.ExcludedData.DevelopmentCapSubjectCount < 0
            || response.ExcludedData.ProductsAbsentFromTrainingInteractions < 0
            || response.ExcludedData.UnknownProductInteractionCount < 0
            || response.ExcludedData.PopularityFallbackSubjectCount < 0
            || response.ReportIdentifier != request.EvaluationVersion
            || response.Reports.JsonFile
                != $"evaluation-{request.EvaluationVersion}.json"
            || response.Reports.MarkdownFile
                != $"evaluation-{request.EvaluationVersion}.md"
            || response.Limitations.Any(string.IsNullOrWhiteSpace)
            || !IsValidEvaluationModel(
                response.Models.Popularity,
                requireAlsParameters: false)
            || !IsValidEvaluationModel(
                response.Models.Als,
                requireAlsParameters: true)
            || !IsExplicitlyNotEvaluated(response.Models.Tfidf)
            || !IsExplicitlyNotEvaluated(response.Models.Fbt))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidEvaluationModel(
        RecommendationModelEvaluationModelResponse? model,
        bool requireAlsParameters)
    {
        if (model is null)
        {
            return false;
        }

        if (model.Status == "NotEvaluated")
        {
            return !string.IsNullOrWhiteSpace(model.Reason)
                && model.Metrics is null;
        }

        if (model.Status != "Evaluated"
            || model.Metrics is null
            || model.Parameters is null
            || string.IsNullOrWhiteSpace(
                model.Parameters.InteractionWeighting)
            || !IsValidEvaluationMetrics(model.Metrics))
        {
            return false;
        }

        return !requireAlsParameters
            || model.Parameters is
            {
                Factors: > 0,
                Regularization: > 0m,
                Iterations: > 0,
                Alpha: > 0m,
                RandomSeed: >= 0
            };
    }

    private static bool IsValidEvaluationMetrics(
        RecommendationModelEvaluationMetricsResponse metrics)
    {
        return IsUnitScore(metrics.PrecisionAtK)
            && IsUnitScore(metrics.RecallAtK)
            && IsUnitScore(metrics.HitRateAtK)
            && IsUnitScore(metrics.NdcgAtK)
            && IsUnitScore(metrics.CatalogueCoverage)
            && IsUnitScore(metrics.KnownSubjectCatalogueCoverage)
            && IsUnitScore(metrics.CatalogueCoverageIncludingFallback)
            && metrics.EligibleSubjectCount >= 0
            && metrics.TrainingInteractionCount >= 0
            && metrics.TestInteractionCount >= 0
            && metrics.TrainingDurationMilliseconds >= 0m
            && metrics.AverageInferenceLatencyMilliseconds >= 0m
            && metrics.P95InferenceLatencyMilliseconds >= 0m
            && metrics.FallbackSubjectCount >= 0;
    }

    private static bool IsExplicitlyNotEvaluated(
        RecommendationModelEvaluationModelResponse? model)
    {
        return model is
        {
            Status: "NotEvaluated",
            Metrics: null,
            Parameters: null
        }
        && !string.IsNullOrWhiteSpace(model.Reason);
    }

    private static bool IsUnitScore(decimal value)
    {
        return value is >= 0m and <= 1m;
    }

    private static bool IsSha256(string value)
    {
        return value.Length == 64
            && value.All(character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
    }

    private static bool IsValidComponentStatus(
        RecommendationModelComponentStatusResponse? status)
    {
        return status is not null
            && status.Status is "Succeeded" or "InsufficientData"
            && status.TrainingDurationMilliseconds >= 0;
    }
}
