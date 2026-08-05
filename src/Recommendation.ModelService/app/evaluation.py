from __future__ import annotations

import hashlib
import json
import math
import os
import re
import tempfile
from dataclasses import dataclass, replace
from datetime import UTC, datetime
from pathlib import Path
from time import perf_counter_ns
from uuid import UUID

from .als import find_personalized, train_als_component
from .config import AlsSettings, EvaluationSettings
from .contracts import (
    EvaluationDatasetCounts,
    EvaluationExcludedDataCounts,
    EvaluationModelMetrics,
    EvaluationModelParameters,
    EvaluationModelResult,
    EvaluationModels,
    EvaluationReportFiles,
    EvaluationSplitSummary,
    ModelEvaluationRequest,
    ModelEvaluationResponse,
    OrderInteractionItem,
    OrderProductInteraction,
)

_SPLIT_STRATEGY = "per_subject_chronological_newest_order_holdout"
_WEIGHT_DESCRIPTION = (
    "1 + log1p(total_quantity) + log1p(max(repeat_purchase_count - 1, 0))"
)
_SUBJECT_PATTERN = re.compile(r"v[1-9][0-9]{0,14}\.[A-Za-z0-9_-]{43}")


@dataclass(frozen=True, slots=True)
class PreparedOrder:
    order_id: UUID
    subject_id: str
    occurred_at_utc: datetime
    items: tuple[OrderInteractionItem, ...]


@dataclass(frozen=True, slots=True)
class TemporalSplit:
    eligible_subject_ids: tuple[str, ...]
    training_orders: tuple[PreparedOrder, ...]
    test_orders: tuple[PreparedOrder, ...]
    training_history: dict[str, frozenset[UUID]]
    relevant_products: dict[str, frozenset[UUID]]
    insufficient_history_subject_count: int
    no_usable_training_history_subject_count: int
    no_usable_test_interactions_subject_count: int
    development_cap_subject_count: int
    unknown_product_interaction_count: int
    products_absent_from_training_interactions: int


class EvaluationReportWriter:
    def __init__(self, directory: Path) -> None:
        self._directory = directory

    def publish(
        self,
        response: ModelEvaluationResponse,
        *,
        subject_ids: tuple[str, ...],
    ) -> None:
        self._directory.mkdir(parents=True, exist_ok=True)
        json_destination = self._directory / response.reports.jsonFile
        markdown_destination = self._directory / response.reports.markdownFile
        if json_destination.exists() or markdown_destination.exists():
            raise FileExistsError("An evaluation report with this version exists.")

        json_content = json.dumps(
            response.model_dump(mode="json"),
            ensure_ascii=False,
            indent=2,
            sort_keys=True,
        )
        markdown_content = render_markdown_report(response)
        _validate_report_privacy(json_content, subject_ids)
        _validate_report_privacy(markdown_content, subject_ids)

        json_temporary = self._write_temporary(json_content, ".json.tmp")
        markdown_temporary = self._write_temporary(
            markdown_content,
            ".md.tmp",
        )
        json_published = False
        try:
            os.replace(json_temporary, json_destination)
            json_published = True
            os.replace(markdown_temporary, markdown_destination)
        except Exception:
            if json_published:
                json_destination.unlink(missing_ok=True)
            json_temporary.unlink(missing_ok=True)
            markdown_temporary.unlink(missing_ok=True)
            raise

    def _write_temporary(self, content: str, suffix: str) -> Path:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            prefix=".evaluation-",
            suffix=suffix,
            dir=self._directory,
            delete=False,
            newline="\n",
        ) as temporary:
            temporary.write(content)
            temporary.write("\n")
            temporary.flush()
            os.fsync(temporary.fileno())
            return Path(temporary.name)


def evaluate_request(
    request: ModelEvaluationRequest,
    *,
    als_settings: AlsSettings,
    evaluation_settings: EvaluationSettings,
    evaluated_at_utc: datetime | None = None,
) -> ModelEvaluationResponse:
    als_settings.validate()
    evaluation_settings.validate()
    evaluated_at = evaluated_at_utc or datetime.now(UTC)
    if evaluated_at.tzinfo is None or evaluated_at.utcoffset() is None:
        raise ValueError("Evaluation timestamp must be offset-aware.")
    evaluated_at = evaluated_at.astimezone(UTC)

    catalogue_product_ids = tuple(
        sorted(request.catalogueProductIds, key=lambda product_id: product_id.hex)
    )
    candidate_product_ids = tuple(
        sorted(request.candidateProductIds, key=lambda product_id: product_id.hex)
    )
    split = build_temporal_split(request, evaluation_settings)
    input_hash = calculate_evaluation_input_hash(request, evaluation_settings)
    training_interaction_count = sum(
        len(order.items) for order in split.training_orders
    )
    test_interaction_count = sum(len(order.items) for order in split.test_orders)
    training_order_count = len(split.training_orders)
    test_order_count = len(split.test_orders)

    popularity_result, popularity_rankings = _evaluate_popularity(
        split,
        candidate_product_ids,
        evaluation_settings,
        training_interaction_count,
        test_interaction_count,
    )
    als_result, fallback_subject_count = _evaluate_als(
        split,
        catalogue_product_ids,
        candidate_product_ids,
        popularity_rankings,
        als_settings,
        evaluation_settings,
        training_interaction_count,
        test_interaction_count,
    )
    json_name = f"evaluation-{request.evaluationVersion}.json"
    markdown_name = f"evaluation-{request.evaluationVersion}.md"
    response = ModelEvaluationResponse(
        status="Succeeded",
        evaluationVersion=request.evaluationVersion,
        evaluatedAtUtc=evaluated_at,
        inputHash=input_hash,
        dataset=EvaluationDatasetCounts(
            productCount=len(catalogue_product_ids),
            candidateProductCount=len(candidate_product_ids),
            subjectCount=len(
                {interaction.subjectId for interaction in request.interactions}
            ),
            orderCount=len(request.interactions),
            interactionCount=sum(
                len(interaction.items) for interaction in request.interactions
            ),
        ),
        split=EvaluationSplitSummary(
            strategy=_SPLIT_STRATEGY,
            description=(
                "Orders are sorted per pseudonymous subject by UTC occurrence "
                "timestamp and OrderId; the newest configured orders are held out."
            ),
            k=evaluation_settings.k,
            minimumHistoricalOrdersPerSubject=(
                evaluation_settings.minimum_historical_orders_per_subject
            ),
            holdoutOrderCount=evaluation_settings.holdout_order_count,
            excludePreviouslyPurchased=(
                evaluation_settings.exclude_previously_purchased
            ),
            randomSeed=evaluation_settings.random_seed,
            eligibleSubjectCount=len(split.eligible_subject_ids),
            excludedSubjectCount=(
                split.insufficient_history_subject_count
                + split.no_usable_training_history_subject_count
                + split.no_usable_test_interactions_subject_count
                + split.development_cap_subject_count
            ),
            trainingOrderCount=training_order_count,
            testOrderCount=test_order_count,
            trainingInteractionCount=training_interaction_count,
            testInteractionCount=test_interaction_count,
        ),
        excludedData=EvaluationExcludedDataCounts(
            insufficientHistorySubjectCount=(
                split.insufficient_history_subject_count
            ),
            noUsableTrainingHistorySubjectCount=(
                split.no_usable_training_history_subject_count
            ),
            noUsableTestInteractionsSubjectCount=(
                split.no_usable_test_interactions_subject_count
            ),
            developmentCapSubjectCount=split.development_cap_subject_count,
            productsAbsentFromTrainingInteractions=(
                split.products_absent_from_training_interactions
            ),
            unknownProductInteractionCount=(
                split.unknown_product_interaction_count
            ),
            popularityFallbackSubjectCount=fallback_subject_count,
        ),
        models=EvaluationModels(
            popularity=popularity_result,
            als=als_result,
            tfidf=_not_evaluated(
                "TF-IDF Similar requires a separate item-to-item relevance protocol."
            ),
            fbt=_not_evaluated(
                "FBT requires a separate deterministic basket-item holdout protocol."
            ),
        ),
        reportIdentifier=request.evaluationVersion,
        reports=EvaluationReportFiles(
            jsonFile=json_name,
            markdownFile=markdown_name,
        ),
        limitations=[
            (
                "Timing measurements are runtime-dependent and excluded from "
                "deterministic comparisons."
            ),
            (
                "Chronological holdout measures offline ranking quality, not causal "
                "business impact."
            ),
            (
                "TF-IDF Similar and FBT are not evaluated without model-appropriate "
                "protocols."
            ),
            "No recency weighting or hybrid ranking is applied in this evaluation.",
        ],
    )
    return response


def build_temporal_split(
    request: ModelEvaluationRequest,
    settings: EvaluationSettings,
) -> TemporalSplit:
    settings.validate()
    catalogue = set(request.catalogueProductIds)
    candidates = set(request.candidateProductIds)
    grouped: dict[str, list[PreparedOrder]] = {}
    unknown_product_interaction_count = 0
    for interaction in request.interactions:
        known_items = tuple(
            item for item in interaction.items if item.productId in catalogue
        )
        unknown_product_interaction_count += len(interaction.items) - len(known_items)
        grouped.setdefault(interaction.subjectId, []).append(
            PreparedOrder(
                order_id=interaction.orderId,
                subject_id=interaction.subjectId,
                occurred_at_utc=interaction.occurredAtUtc,
                items=known_items,
            )
        )

    subject_ids = sorted(grouped)
    capped_subjects: set[str] = set()
    if (
        settings.maximum_subjects is not None
        and len(subject_ids) > settings.maximum_subjects
    ):
        capped_subjects = set(subject_ids[settings.maximum_subjects :])
        subject_ids = subject_ids[: settings.maximum_subjects]

    eligible: list[str] = []
    training_orders: list[PreparedOrder] = []
    test_orders: list[PreparedOrder] = []
    training_history: dict[str, frozenset[UUID]] = {}
    relevant_products: dict[str, frozenset[UUID]] = {}
    insufficient_history = 0
    no_training = 0
    no_test = 0
    for subject_id in subject_ids:
        orders = sorted(
            grouped[subject_id],
            key=lambda order: (order.occurred_at_utc, order.order_id.hex),
        )
        if (
            len(orders) < settings.minimum_historical_orders_per_subject
            or len(orders) <= settings.holdout_order_count
        ):
            insufficient_history += 1
            continue
        subject_training = orders[: -settings.holdout_order_count]
        subject_test = orders[-settings.holdout_order_count :]
        purchased = frozenset(
            item.productId for order in subject_training for item in order.items
        )
        if not purchased:
            no_training += 1
            continue
        relevant = frozenset(
            item.productId
            for order in subject_test
            for item in order.items
            if item.productId in candidates
        )
        if not relevant:
            no_test += 1
            continue
        eligible.append(subject_id)
        training_orders.extend(subject_training)
        test_orders.extend(
            PreparedOrder(
                order_id=order.order_id,
                subject_id=order.subject_id,
                occurred_at_utc=order.occurred_at_utc,
                items=tuple(
                    item for item in order.items if item.productId in candidates
                ),
            )
            for order in subject_test
        )
        training_history[subject_id] = purchased
        relevant_products[subject_id] = relevant

    interacted_products = {
        item.productId for order in training_orders for item in order.items
    }
    return TemporalSplit(
        eligible_subject_ids=tuple(eligible),
        training_orders=tuple(training_orders),
        test_orders=tuple(test_orders),
        training_history=training_history,
        relevant_products=relevant_products,
        insufficient_history_subject_count=insufficient_history,
        no_usable_training_history_subject_count=no_training,
        no_usable_test_interactions_subject_count=no_test,
        development_cap_subject_count=len(capped_subjects),
        unknown_product_interaction_count=unknown_product_interaction_count,
        products_absent_from_training_interactions=(
            len(catalogue - interacted_products)
        ),
    )


def calculate_evaluation_input_hash(
    request: ModelEvaluationRequest,
    settings: EvaluationSettings,
) -> str:
    interactions = []
    for interaction in sorted(
        request.interactions,
        key=lambda item: (
            item.subjectId,
            item.occurredAtUtc,
            item.orderId.hex,
        ),
    ):
        interactions.append(
            {
                "orderId": str(interaction.orderId),
                "subjectId": interaction.subjectId,
                "occurredAtUtc": interaction.occurredAtUtc.isoformat(),
                "items": [
                    {
                        "productId": str(item.productId),
                        "quantity": item.quantity,
                    }
                    for item in sorted(
                        interaction.items,
                        key=lambda item: item.productId.hex,
                    )
                ],
            }
        )
    canonical = {
        "catalogueProductIds": sorted(
            str(product_id) for product_id in request.catalogueProductIds
        ),
        "candidateProductIds": sorted(
            str(product_id) for product_id in request.candidateProductIds
        ),
        "interactions": interactions,
        "configuration": {
            "k": settings.k,
            "minimumHistoricalOrdersPerSubject": (
                settings.minimum_historical_orders_per_subject
            ),
            "holdoutOrderCount": settings.holdout_order_count,
            "excludePreviouslyPurchased": settings.exclude_previously_purchased,
            "randomSeed": settings.random_seed,
            "maximumSubjects": settings.maximum_subjects,
        },
    }
    encoded = json.dumps(
        canonical,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def ranking_metrics(
    rankings: dict[str, tuple[UUID, ...]],
    relevant_products: dict[str, frozenset[UUID]],
    *,
    k: int,
) -> tuple[float, float, float, float]:
    if not rankings:
        return 0.0, 0.0, 0.0, 0.0
    precisions: list[float] = []
    recalls: list[float] = []
    hits: list[float] = []
    ndcgs: list[float] = []
    for subject_id in sorted(rankings):
        ranking = rankings[subject_id][:k]
        relevant = relevant_products[subject_id]
        hit_count = len(set(ranking) & relevant)
        precisions.append(hit_count / k)
        recalls.append(hit_count / len(relevant))
        hits.append(1.0 if hit_count else 0.0)
        dcg = sum(
            1.0 / math.log2(index + 2)
            for index, product_id in enumerate(ranking)
            if product_id in relevant
        )
        ideal_count = min(k, len(relevant))
        ideal_dcg = sum(
            1.0 / math.log2(index + 2) for index in range(ideal_count)
        )
        ndcgs.append(dcg / ideal_dcg if ideal_dcg else 0.0)
    count = len(rankings)
    return (
        sum(precisions) / count,
        sum(recalls) / count,
        sum(hits) / count,
        sum(ndcgs) / count,
    )


def p95_latency(milliseconds: list[float]) -> float:
    if not milliseconds:
        return 0.0
    ordered = sorted(milliseconds)
    index = max(0, math.ceil(0.95 * len(ordered)) - 1)
    return ordered[index]


def render_markdown_report(response: ModelEvaluationResponse) -> str:
    lines = [
        f"# Recommendation Evaluation {response.evaluationVersion}",
        "",
        f"- Status: `{response.status}`",
        f"- Evaluated at UTC: `{response.evaluatedAtUtc.isoformat()}`",
        f"- Input hash: `{response.inputHash}`",
        f"- Split: `{response.split.strategy}`",
        "",
        "## Dataset and split",
        "",
        "| Measure | Value |",
        "|---|---:|",
        f"| Products | {response.dataset.productCount} |",
        f"| Candidate products | {response.dataset.candidateProductCount} |",
        f"| Pseudonymous subjects | {response.dataset.subjectCount} |",
        f"| Orders | {response.dataset.orderCount} |",
        f"| Order-item interactions | {response.dataset.interactionCount} |",
        f"| Eligible subjects | {response.split.eligibleSubjectCount} |",
        f"| Excluded subjects | {response.split.excludedSubjectCount} |",
        f"| Training orders | {response.split.trainingOrderCount} |",
        f"| Test orders | {response.split.testOrderCount} |",
        f"| Training interactions | {response.split.trainingInteractionCount} |",
        f"| Test interactions | {response.split.testInteractionCount} |",
        "",
        "## Model metrics",
        "",
        (
            "| Model | Status | Precision@K | Recall@K | HitRate@K | NDCG@K | "
            "Coverage | Coverage with fallback | Train ms* | Avg infer ms* | "
            "P95 infer ms* |"
        ),
        "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for name, result in (
        ("Popularity", response.models.popularity),
        ("ALS", response.models.als),
        ("TF-IDF Similar", response.models.tfidf),
        ("FBT", response.models.fbt),
    ):
        if result.metrics is None:
            lines.append(
                f"| {name} | {result.status}: {result.reason or ''} | - | - | - | "
                "- | - | - | - | - | - |"
            )
            continue
        metrics = result.metrics
        lines.append(
            f"| {name} | {result.status} | {metrics.precisionAtK!r} | "
            f"{metrics.recallAtK!r} | {metrics.hitRateAtK!r} | "
            f"{metrics.ndcgAtK!r} | {metrics.catalogueCoverage!r} | "
            f"{metrics.catalogueCoverageIncludingFallback!r} | "
            f"{metrics.trainingDurationMilliseconds!r} | "
            f"{metrics.averageInferenceLatencyMilliseconds!r} | "
            f"{metrics.p95InferenceLatencyMilliseconds!r} |"
        )
    lines.extend(
        [
            "",
            "`*` Timing values are runtime-dependent.",
            "",
            "## Metric definitions",
            "",
            f"- Precision@{response.split.k}: relevant recommendations divided by K.",
            (
                f"- Recall@{response.split.k}: relevant recommendations divided by "
                "unique relevant held-out products."
            ),
            (
                f"- HitRate@{response.split.k}: fraction of eligible subjects with at "
                "least one relevant recommendation."
            ),
            (
                f"- NDCG@{response.split.k}: binary discounted cumulative gain divided "
                "by the ideal gain."
            ),
            (
                "- Catalogue coverage: distinct recommended candidate products divided "
                "by candidate catalogue size."
            ),
            "",
            "## Excluded data",
            "",
            (
                "- Insufficient history subjects: "
                f"{response.excludedData.insufficientHistorySubjectCount}"
            ),
            (
                "- No usable training history: "
                f"{response.excludedData.noUsableTrainingHistorySubjectCount}"
            ),
            (
                "- No usable test interactions: "
                f"{response.excludedData.noUsableTestInteractionsSubjectCount}"
            ),
            (
                "- Development cap exclusions: "
                f"{response.excludedData.developmentCapSubjectCount}"
            ),
            (
                "- Products absent from training interactions: "
                f"{response.excludedData.productsAbsentFromTrainingInteractions}"
            ),
            (
                "- Unknown product interactions ignored: "
                f"{response.excludedData.unknownProductInteractionCount}"
            ),
            (
                "- ALS subjects served by popularity fallback: "
                f"{response.excludedData.popularityFallbackSubjectCount}"
            ),
            "",
            "## Limitations",
            "",
        ]
    )
    lines.extend(f"- {limitation}" for limitation in response.limitations)
    return "\n".join(lines)


def _evaluate_popularity(
    split: TemporalSplit,
    candidate_product_ids: tuple[UUID, ...],
    settings: EvaluationSettings,
    training_interaction_count: int,
    test_interaction_count: int,
) -> tuple[EvaluationModelResult, dict[str, tuple[UUID, ...]]]:
    parameters = EvaluationModelParameters(
        interactionWeighting="total_quantity",
        excludePreviouslyPurchased=settings.exclude_previously_purchased,
    )
    if not split.eligible_subject_ids:
        return (
            EvaluationModelResult(
                status="NotEvaluated",
                reason="No subjects have usable chronological training and test data.",
                parameters=parameters,
            ),
            {},
        )
    started = perf_counter_ns()
    counts = {product_id: 0 for product_id in candidate_product_ids}
    for order in split.training_orders:
        for item in order.items:
            if item.productId in counts:
                counts[item.productId] += item.quantity
    training_duration = _elapsed_milliseconds(started)
    rankings: dict[str, tuple[UUID, ...]] = {}
    latencies: list[float] = []
    for subject_id in split.eligible_subject_ids:
        inference_started = perf_counter_ns()
        rankings[subject_id] = _popularity_ranking(
            counts,
            split.training_history[subject_id],
            settings,
        )
        latencies.append(_elapsed_milliseconds(inference_started))
    metrics = _build_metrics(
        rankings,
        split.relevant_products,
        candidate_count=len(candidate_product_ids),
        known_rankings=rankings,
        k=settings.k,
        training_interaction_count=training_interaction_count,
        test_interaction_count=test_interaction_count,
        training_duration=training_duration,
        latencies=latencies,
        fallback_subject_count=0,
    )
    return (
        EvaluationModelResult(
            status="Evaluated",
            metrics=metrics,
            parameters=parameters,
        ),
        rankings,
    )


def _evaluate_als(
    split: TemporalSplit,
    catalogue_product_ids: tuple[UUID, ...],
    candidate_product_ids: tuple[UUID, ...],
    popularity_rankings: dict[str, tuple[UUID, ...]],
    als_settings: AlsSettings,
    evaluation_settings: EvaluationSettings,
    training_interaction_count: int,
    test_interaction_count: int,
) -> tuple[EvaluationModelResult, int]:
    evaluation_als_settings = replace(
        als_settings,
        random_seed=evaluation_settings.random_seed,
    )
    parameters = EvaluationModelParameters(
        interactionWeighting=_WEIGHT_DESCRIPTION,
        excludePreviouslyPurchased=(
            evaluation_settings.exclude_previously_purchased
        ),
        factors=evaluation_als_settings.factors,
        regularization=evaluation_als_settings.regularization,
        iterations=evaluation_als_settings.iterations,
        alpha=evaluation_als_settings.alpha,
        randomSeed=evaluation_als_settings.random_seed,
    )
    if not split.eligible_subject_ids:
        return (
            EvaluationModelResult(
                status="NotEvaluated",
                reason="No subjects have usable chronological training and test data.",
                parameters=parameters,
            ),
            0,
        )
    training_contract = [
        OrderProductInteraction(
            orderId=order.order_id,
            subjectId=order.subject_id,
            items=list(order.items),
        )
        for order in split.training_orders
    ]
    started = perf_counter_ns()
    component, _ = train_als_component(
        training_contract,
        catalogue_product_ids,
        evaluation_als_settings,
    )
    training_duration = _elapsed_milliseconds(started)
    if component is None:
        return (
            EvaluationModelResult(
                status="NotEvaluated",
                reason="The chronological training split is insufficient for ALS.",
                parameters=parameters,
            ),
            0,
        )
    candidate_set = set(candidate_product_ids)
    availability = tuple(
        product_id in candidate_set for product_id in catalogue_product_ids
    )
    rankings: dict[str, tuple[UUID, ...]] = {}
    known_rankings: dict[str, tuple[UUID, ...]] = {}
    latencies: list[float] = []
    fallback_subject_count = 0
    for subject_id in split.eligible_subject_ids:
        inference_started = perf_counter_ns()
        recommendations = find_personalized(
            component,
            subject_id,
            availability,
            evaluation_settings.k,
            exclude_previously_purchased=(
                evaluation_settings.exclude_previously_purchased
            ),
        )
        primary = tuple(
            item.productId for item in recommendations or []
        )
        known_rankings[subject_id] = primary
        if primary:
            rankings[subject_id] = primary
        else:
            rankings[subject_id] = popularity_rankings.get(subject_id, ())
            fallback_subject_count += 1
        latencies.append(_elapsed_milliseconds(inference_started))
    metrics = _build_metrics(
        rankings,
        split.relevant_products,
        candidate_count=len(candidate_product_ids),
        known_rankings=known_rankings,
        k=evaluation_settings.k,
        training_interaction_count=training_interaction_count,
        test_interaction_count=test_interaction_count,
        training_duration=training_duration,
        latencies=latencies,
        fallback_subject_count=fallback_subject_count,
    )
    return (
        EvaluationModelResult(
            status="Evaluated",
            metrics=metrics,
            parameters=parameters,
        ),
        fallback_subject_count,
    )


def _popularity_ranking(
    counts: dict[UUID, int],
    purchased: frozenset[UUID],
    settings: EvaluationSettings,
) -> tuple[UUID, ...]:
    candidates = (
        product_id
        for product_id in counts
        if not settings.exclude_previously_purchased or product_id not in purchased
    )
    ordered = sorted(
        candidates,
        key=lambda product_id: (-counts[product_id], product_id.hex),
    )
    return tuple(ordered[: settings.k])


def _build_metrics(
    rankings: dict[str, tuple[UUID, ...]],
    relevant_products: dict[str, frozenset[UUID]],
    *,
    candidate_count: int,
    known_rankings: dict[str, tuple[UUID, ...]],
    k: int,
    training_interaction_count: int,
    test_interaction_count: int,
    training_duration: float,
    latencies: list[float],
    fallback_subject_count: int,
) -> EvaluationModelMetrics:
    precision, recall, hit_rate, ndcg = ranking_metrics(
        rankings,
        relevant_products,
        k=k,
    )
    recommended = {
        product_id for ranking in rankings.values() for product_id in ranking
    }
    known_recommended = {
        product_id for ranking in known_rankings.values() for product_id in ranking
    }
    denominator = candidate_count or 1
    return EvaluationModelMetrics(
        precisionAtK=precision,
        recallAtK=recall,
        hitRateAtK=hit_rate,
        ndcgAtK=ndcg,
        catalogueCoverage=len(known_recommended) / denominator,
        knownSubjectCatalogueCoverage=len(known_recommended) / denominator,
        catalogueCoverageIncludingFallback=len(recommended) / denominator,
        eligibleSubjectCount=len(rankings),
        trainingInteractionCount=training_interaction_count,
        testInteractionCount=test_interaction_count,
        trainingDurationMilliseconds=training_duration,
        averageInferenceLatencyMilliseconds=(
            sum(latencies) / len(latencies) if latencies else 0.0
        ),
        p95InferenceLatencyMilliseconds=p95_latency(latencies),
        fallbackSubjectCount=fallback_subject_count,
    )


def _not_evaluated(reason: str) -> EvaluationModelResult:
    return EvaluationModelResult(status="NotEvaluated", reason=reason)


def _elapsed_milliseconds(started: int) -> float:
    return (perf_counter_ns() - started) / 1_000_000


def _validate_report_privacy(content: str, subject_ids: tuple[str, ...]) -> None:
    lowered = content.lower()
    if "customerid" in lowered or "subjectid" in lowered:
        raise ValueError("Evaluation report contains a forbidden identifier field.")
    if _SUBJECT_PATTERN.search(content) or any(
        subject_id in content for subject_id in subject_ids
    ):
        raise ValueError("Evaluation report contains a pseudonymous identifier value.")
