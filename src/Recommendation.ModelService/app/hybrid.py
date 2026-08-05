from __future__ import annotations

import math
from collections import Counter
from dataclasses import dataclass
from uuid import UUID

import numpy as np
from scipy.sparse import csr_matrix
from sklearn.metrics.pairwise import cosine_similarity

from .als import TrainedAlsComponent
from .config import HybridSettings
from .contracts import (
    OrderProductInteraction,
    PersonalizedRecommendationItem,
    SimilarProductItem,
)

ALGORITHM = "deterministic-hybrid-ranker-v1"
POPULARITY_ALGORITHM = "quantity-popularity-v1"
ASSOCIATION_ALGORITHM = "directional-copurchase-v1"
PERSONALIZED_STRATEGY = "HybridPersonalized"
SIMILAR_STRATEGY = "HybridSimilar"


@dataclass(frozen=True, slots=True)
class ScoredProductIndex:
    product_index: int
    score: float


@dataclass(frozen=True, slots=True)
class TrainedHybridComponent:
    popularity_scores: tuple[float, ...]
    association_neighbors: tuple[tuple[ScoredProductIndex, ...], ...]
    content_neighbors: tuple[tuple[ScoredProductIndex, ...], ...]
    settings: HybridSettings


def normalize_component_scores(
    scores: dict[int, float],
) -> dict[int, float]:
    if not scores:
        return {}
    if any(not math.isfinite(score) for score in scores.values()):
        raise ValueError("Hybrid component scores must be finite.")
    minimum = min(scores.values())
    maximum = max(scores.values())
    if math.isclose(minimum, maximum, rel_tol=0.0, abs_tol=1e-12):
        value = 1.0 if maximum > 0.0 else 0.0
        return {index: value for index in scores}
    scale = maximum - minimum
    return {
        index: float(np.clip((score - minimum) / scale, 0.0, 1.0))
        for index, score in scores.items()
    }


def deterministic_candidate_union(
    component_scores: tuple[dict[int, float], ...],
    *,
    pool_size: int,
) -> tuple[int, ...]:
    if pool_size <= 0:
        raise ValueError("Hybrid candidate pool size must be positive.")
    candidates: set[int] = set()
    for scores in component_scores:
        ranked = sorted(scores.items(), key=lambda item: (-item[1], item[0]))
        candidates.update(index for index, _ in ranked[:pool_size])
    if len(candidates) > pool_size:
        candidates = set(
            sorted(
                candidates,
                key=lambda index: (
                    -max(
                        (scores.get(index, 0.0) for scores in component_scores),
                        default=0.0,
                    ),
                    index,
                ),
            )[:pool_size]
        )
    return tuple(sorted(candidates))


def build_hybrid_component(
    product_ids: tuple[UUID, ...],
    matrix: csr_matrix,
    interactions: list[OrderProductInteraction],
    settings: HybridSettings,
) -> TrainedHybridComponent:
    settings.validate()
    if matrix.shape[0] != len(product_ids):
        raise ValueError("Hybrid content dimensions are inconsistent.")
    product_indices = {
        product_id: index for index, product_id in enumerate(product_ids)
    }
    popularity_counts = {index: 0.0 for index in range(len(product_ids))}
    baskets: list[frozenset[int]] = []
    for interaction in interactions:
        basket: set[int] = set()
        for item in interaction.items:
            index = product_indices[item.productId]
            popularity_counts[index] += item.quantity
            basket.add(index)
        if basket:
            baskets.append(frozenset(basket))
    popularity = normalize_component_scores(popularity_counts)
    association = _build_association_neighbors(
        len(product_ids),
        baskets,
        settings.candidate_pool_cap,
    )
    content = _build_content_neighbors(
        matrix,
        settings.candidate_pool_cap,
    )
    return TrainedHybridComponent(
        popularity_scores=tuple(
            round(popularity[index], 12) for index in range(len(product_ids))
        ),
        association_neighbors=association,
        content_neighbors=content,
        settings=settings,
    )


def find_hybrid_personalized(
    component: TrainedHybridComponent,
    als: TrainedAlsComponent,
    subject_id: str,
    availability: tuple[bool, ...],
    limit: int,
    *,
    exclude_previously_purchased: bool,
) -> list[PersonalizedRecommendationItem] | None:
    try:
        subject_index = als.subject_ids.index(subject_id)
    except ValueError:
        return None
    _validate_dimensions(component, als.product_ids, availability)
    purchased = als.purchased_product_indices[subject_index]
    eligible = tuple(
        index
        for index, available in enumerate(availability)
        if available
        and (not exclude_previously_purchased or index not in purchased)
    )
    if not eligible:
        return []
    raw_als = np.asarray(
        als.model.user_factors[subject_index] @ als.model.item_factors.T,
        dtype=np.float64,
    )
    als_scores = normalize_component_scores(
        {index: float(raw_als[index]) for index in eligible}
    )
    content_scores = normalize_component_scores(
        _aggregate_neighbor_scores(
            component.content_neighbors,
            purchased,
            set(eligible),
        )
    )
    association_scores = normalize_component_scores(
        _aggregate_neighbor_scores(
            component.association_neighbors,
            purchased,
            set(eligible),
        )
    )
    popularity_scores = normalize_component_scores(
        {index: component.popularity_scores[index] for index in eligible}
    )
    pool_size = _candidate_pool_size(component.settings, limit)
    candidates = deterministic_candidate_union(
        (
            als_scores,
            content_scores,
            association_scores,
            popularity_scores,
        ),
        pool_size=pool_size,
    )
    weights = component.settings.personalized
    ranked: list[tuple[float, UUID, PersonalizedRecommendationItem]] = []
    for index in candidates:
        als_score = als_scores.get(index, 0.0)
        content_score = content_scores.get(index, 0.0)
        association_score = association_scores.get(index, 0.0)
        popularity_score = popularity_scores.get(index, 0.0)
        contributions = (
            weights.als * als_score,
            weights.content * content_score,
            weights.association * association_score,
            weights.popularity * popularity_score,
        )
        final_score = round(sum(contributions), 12)
        reason_code, reason_text = _personalized_reason(contributions)
        product_id = als.product_ids[index]
        ranked.append(
            (
                final_score,
                product_id,
                PersonalizedRecommendationItem(
                    productId=product_id,
                    score=final_score,
                    confidence=None,
                    reasonCode=reason_code,
                    reasonText=reason_text,
                    alsScore=round(als_score, 12),
                    contentAffinityScore=round(content_score, 12),
                    associationScore=round(association_score, 12),
                    popularityScore=round(popularity_score, 12),
                    finalScore=final_score,
                ),
            )
        )
    ranked.sort(key=lambda item: (-item[0], item[1].hex))
    return [item for _, _, item in ranked[:limit]]


def find_hybrid_similar(
    component: TrainedHybridComponent,
    product_ids: tuple[UUID, ...],
    availability: tuple[bool, ...],
    source_product_id: UUID,
    limit: int,
) -> list[SimilarProductItem]:
    try:
        source_index = product_ids.index(source_product_id)
    except ValueError:
        return []
    _validate_dimensions(component, product_ids, availability)
    eligible = {
        index
        for index, available in enumerate(availability)
        if available and index != source_index
    }
    content_scores = normalize_component_scores(
        {
            item.product_index: item.score
            for item in component.content_neighbors[source_index]
            if item.product_index in eligible
        }
    )
    association_scores = normalize_component_scores(
        {
            item.product_index: item.score
            for item in component.association_neighbors[source_index]
            if item.product_index in eligible
        }
    )
    popularity_scores = normalize_component_scores(
        {index: component.popularity_scores[index] for index in eligible}
    )
    pool_size = _candidate_pool_size(component.settings, limit)
    candidates = deterministic_candidate_union(
        (content_scores, association_scores, popularity_scores),
        pool_size=pool_size,
    )
    weights = component.settings.similar
    ranked: list[tuple[float, UUID, SimilarProductItem]] = []
    for index in candidates:
        content_score = content_scores.get(index, 0.0)
        association_score = association_scores.get(index, 0.0)
        popularity_score = popularity_scores.get(index, 0.0)
        contributions = (
            weights.content * content_score,
            weights.association * association_score,
            weights.popularity * popularity_score,
        )
        final_score = round(sum(contributions), 12)
        reason_code, reason_text = _similar_reason(contributions)
        product_id = product_ids[index]
        ranked.append(
            (
                final_score,
                product_id,
                SimilarProductItem(
                    productId=product_id,
                    tfidfScore=round(content_score, 12),
                    coPurchaseScore=round(association_score, 12),
                    popularityScore=round(popularity_score, 12),
                    finalScore=final_score,
                    reasonCode=reason_code,
                    reasonText=reason_text,
                ),
            )
        )
    ranked.sort(key=lambda item: (-item[0], item[1].hex))
    return [item for _, _, item in ranked[:limit]]


def _build_content_neighbors(
    matrix: csr_matrix,
    cap: int,
) -> tuple[tuple[ScoredProductIndex, ...], ...]:
    rows: list[tuple[ScoredProductIndex, ...]] = []
    for source_index in range(matrix.shape[0]):
        similarities = cosine_similarity(
            matrix[source_index],
            matrix,
            dense_output=False,
        ).tocsr()
        candidates = [
            ScoredProductIndex(int(index), round(float(score), 12))
            for index, score in zip(
                similarities.indices,
                similarities.data,
                strict=True,
            )
            if index != source_index
            and math.isfinite(float(score))
            and score > 0.0
        ]
        candidates.sort(key=lambda item: (-item.score, item.product_index))
        rows.append(tuple(candidates[:cap]))
    return tuple(rows)


def _build_association_neighbors(
    product_count: int,
    baskets: list[frozenset[int]],
    cap: int,
) -> tuple[tuple[ScoredProductIndex, ...], ...]:
    order_counts: Counter[int] = Counter()
    pair_counts: Counter[tuple[int, int]] = Counter()
    for basket in baskets:
        ordered = sorted(basket)
        order_counts.update(ordered)
        for source in ordered:
            for target in ordered:
                if source != target:
                    pair_counts[(source, target)] += 1
    total_orders = len(baskets)
    raw_by_source: list[dict[int, float]] = [
        {} for _ in range(product_count)
    ]
    if total_orders:
        for (source, target), count in sorted(pair_counts.items()):
            support = count / total_orders
            confidence = count / order_counts[source]
            target_probability = order_counts[target] / total_orders
            lift = confidence / target_probability
            raw_by_source[source][target] = (
                support * confidence * math.log1p(lift)
            )
    rows: list[tuple[ScoredProductIndex, ...]] = []
    for raw in raw_by_source:
        normalized = normalize_component_scores(raw)
        ranked = [
            ScoredProductIndex(index, round(score, 12))
            for index, score in normalized.items()
            if score > 0.0
        ]
        ranked.sort(key=lambda item: (-item.score, item.product_index))
        rows.append(tuple(ranked[:cap]))
    return tuple(rows)


def _aggregate_neighbor_scores(
    neighbors: tuple[tuple[ScoredProductIndex, ...], ...],
    sources: frozenset[int],
    eligible: set[int],
) -> dict[int, float]:
    result: dict[int, float] = {}
    for source in sorted(sources):
        for item in neighbors[source]:
            if item.product_index in eligible:
                result[item.product_index] = max(
                    result.get(item.product_index, 0.0),
                    item.score,
                )
    return result


def _candidate_pool_size(settings: HybridSettings, limit: int) -> int:
    return min(
        settings.candidate_pool_cap,
        max(limit, limit * settings.candidate_pool_multiplier),
    )


def _validate_dimensions(
    component: TrainedHybridComponent,
    product_ids: tuple[UUID, ...],
    availability: tuple[bool, ...],
) -> None:
    count = len(product_ids)
    if (
        len(availability) != count
        or len(component.popularity_scores) != count
        or len(component.association_neighbors) != count
        or len(component.content_neighbors) != count
    ):
        raise ValueError("Hybrid artifact dimensions are inconsistent.")


def _personalized_reason(
    contributions: tuple[float, float, float, float],
) -> tuple[str, str]:
    strongest = max(range(len(contributions)), key=lambda index: contributions[index])
    return (
        (
            "Hybrid.AlsDominant",
            "Ranked primarily by implicit purchase affinity.",
        ),
        (
            "Hybrid.ContentAffinity",
            "Ranked primarily by content affinity to prior purchases.",
        ),
        (
            "Hybrid.Association",
            "Ranked primarily by directional co-purchase affinity.",
        ),
        (
            "Hybrid.PopularitySupport",
            "Ranked primarily by normalized purchase popularity.",
        ),
    )[strongest]


def _similar_reason(
    contributions: tuple[float, float, float],
) -> tuple[str, str]:
    strongest = max(range(len(contributions)), key=lambda index: contributions[index])
    return (
        (
            "Hybrid.Similar",
            "Ranked primarily by product-content similarity.",
        ),
        (
            "Hybrid.Association",
            "Ranked primarily by directional co-purchase similarity.",
        ),
        (
            "Hybrid.PopularitySupport",
            "Ranked primarily by normalized purchase popularity.",
        ),
    )[strongest]
