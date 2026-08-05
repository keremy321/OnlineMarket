from __future__ import annotations

import math
from collections import defaultdict
from dataclasses import dataclass
from uuid import UUID

import numpy as np
from implicit.als import AlternatingLeastSquares
from implicit.cpu.als import AlternatingLeastSquares as CpuAlternatingLeastSquares
from scipy.sparse import csr_matrix
from threadpoolctl import threadpool_limits

from .config import AlsSettings
from .contracts import OrderProductInteraction, PersonalizedRecommendationItem

ALGORITHM = "implicit-als-v1"
PERSONALIZED_STRATEGY = "implicit_als"
COLD_START_STRATEGY = "cold_start_unavailable"


@dataclass(frozen=True, slots=True)
class AggregatedInteraction:
    subject_id: str
    product_id: UUID
    total_quantity: int
    repeat_purchase_count: int
    weight: float


@dataclass(frozen=True, slots=True)
class TrainedAlsComponent:
    model: CpuAlternatingLeastSquares
    subject_ids: tuple[str, ...]
    product_ids: tuple[UUID, ...]
    purchased_product_indices: tuple[frozenset[int], ...]


def aggregate_interactions(
    interactions: list[OrderProductInteraction],
) -> tuple[AggregatedInteraction, ...]:
    totals: dict[tuple[str, UUID], list[int]] = defaultdict(lambda: [0, 0])
    for interaction in interactions:
        for item in interaction.items:
            aggregate = totals[(interaction.subjectId, item.productId)]
            aggregate[0] += item.quantity
            aggregate[1] += 1

    return tuple(
        AggregatedInteraction(
            subject_id=subject_id,
            product_id=product_id,
            total_quantity=values[0],
            repeat_purchase_count=values[1],
            weight=interaction_weight(values[0], values[1]),
        )
        for (subject_id, product_id), values in sorted(
            totals.items(),
            key=lambda item: (item[0][0], item[0][1].hex),
        )
    )


def interaction_weight(
    total_quantity: int,
    repeat_purchase_count: int,
) -> float:
    if total_quantity <= 0 or repeat_purchase_count <= 0:
        raise ValueError("Interaction totals and repeat counts must be positive.")
    repeat_purchase_weight = math.log1p(max(0, repeat_purchase_count - 1))
    return 1.0 + math.log1p(total_quantity) + repeat_purchase_weight


def train_als_component(
    interactions: list[OrderProductInteraction],
    product_ids: tuple[UUID, ...],
    settings: AlsSettings,
) -> tuple[TrainedAlsComponent | None, tuple[AggregatedInteraction, ...]]:
    settings.validate()
    aggregated = aggregate_interactions(interactions)
    subjects = tuple(sorted({item.subject_id for item in aggregated}))
    interacted_products = {item.product_id for item in aggregated}
    if (
        len(subjects) < settings.minimum_subjects
        or len(interacted_products) < settings.minimum_products
        or len(aggregated) < settings.minimum_interactions
    ):
        return None, aggregated

    subject_indices = {subject_id: index for index, subject_id in enumerate(subjects)}
    product_indices = {
        product_id: index for index, product_id in enumerate(product_ids)
    }
    rows = [subject_indices[item.subject_id] for item in aggregated]
    columns = [product_indices[item.product_id] for item in aggregated]
    values = [item.weight for item in aggregated]
    user_items = csr_matrix(
        (values, (rows, columns)),
        shape=(len(subjects), len(product_ids)),
        dtype=np.float32,
    )
    with threadpool_limits(limits=1, user_api="blas"):
        model = AlternatingLeastSquares(
            factors=settings.factors,
            regularization=settings.regularization,
            iterations=settings.iterations,
            alpha=settings.alpha,
            random_state=settings.random_seed,
            num_threads=1,
        )
        model.fit(user_items, show_progress=False)
    purchased: list[set[int]] = [set() for _ in subjects]
    for row, column in zip(rows, columns, strict=True):
        purchased[row].add(column)
    return (
        TrainedAlsComponent(
            model=model,
            subject_ids=subjects,
            product_ids=product_ids,
            purchased_product_indices=tuple(
                frozenset(indices) for indices in purchased
            ),
        ),
        aggregated,
    )


def find_personalized(
    component: TrainedAlsComponent | None,
    subject_id: str,
    availability: tuple[bool, ...],
    limit: int,
    *,
    exclude_previously_purchased: bool,
) -> list[PersonalizedRecommendationItem] | None:
    if component is None:
        return None
    try:
        subject_index = component.subject_ids.index(subject_id)
    except ValueError:
        return None
    if len(availability) != len(component.product_ids):
        raise ValueError("ALS availability dimensions are inconsistent.")

    raw_scores = np.asarray(
        component.model.user_factors[subject_index] @ component.model.item_factors.T,
        dtype=np.float64,
    )
    purchased = component.purchased_product_indices[subject_index]
    candidates = [
        (index, float(raw_scores[index]))
        for index in range(len(component.product_ids))
        if availability[index]
        and (not exclude_previously_purchased or index not in purchased)
        and math.isfinite(float(raw_scores[index]))
    ]
    if not candidates:
        return []
    normalized = _normalize_scores(candidates)
    ranked = sorted(
        (
            component.product_ids[index],
            round(score, 12),
        )
        for index, score in normalized.items()
    )
    ranked.sort(key=lambda item: (-item[1], item[0].hex))
    return [
        PersonalizedRecommendationItem(
            productId=product_id,
            score=score,
            confidence=None,
            reasonCode="Personalized.ImplicitAls",
            reasonText="Recommended from pseudonymous purchase interactions.",
        )
        for product_id, score in ranked[:limit]
    ]


def _normalize_scores(
    candidates: list[tuple[int, float]],
) -> dict[int, float]:
    minimum = min(score for _, score in candidates)
    maximum = max(score for _, score in candidates)
    if math.isclose(minimum, maximum):
        value = 1.0 if maximum > 0 else 0.0
        return {index: value for index, _ in candidates}
    scale = maximum - minimum
    return {
        index: float(np.clip((score - minimum) / scale, 0.0, 1.0))
        for index, score in candidates
    }
