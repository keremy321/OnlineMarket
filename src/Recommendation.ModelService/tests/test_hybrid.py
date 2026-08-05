from __future__ import annotations

from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from uuid import UUID

import numpy as np
import pytest

from app.als import TrainedAlsComponent
from app.artifacts import ArtifactStore
from app.config import (
    HybridSettings,
    PersonalizedHybridWeights,
    SimilarHybridWeights,
)
from app.contracts import ModelTrainingRequest, PersonalizedModelStrategy
from app.hybrid import (
    ScoredProductIndex,
    TrainedHybridComponent,
    deterministic_candidate_union,
    find_hybrid_personalized,
    find_hybrid_similar,
    normalize_component_scores,
)
from app.model import train_model
from app.service import RecommendationModelService


@pytest.mark.parametrize(
    "weights",
    [
        PersonalizedHybridWeights(0.5, 0.2, 0.15, 0.14),
        PersonalizedHybridWeights(float("nan"), 0.2, 0.15, 0.15),
        PersonalizedHybridWeights(-0.01, 0.21, 0.3, 0.5),
        SimilarHybridWeights(0.7, 0.2, 0.11),
        SimilarHybridWeights(float("inf"), 0.0, 0.0),
    ],
)
def test_hybrid_weight_validation_rejects_invalid_values(
    weights: PersonalizedHybridWeights | SimilarHybridWeights,
) -> None:
    with pytest.raises(ValueError):
        weights.validate()


def test_hybrid_weight_validation_uses_explicit_tolerance() -> None:
    PersonalizedHybridWeights(
        0.5,
        0.2,
        0.15,
        0.1500000005,
    ).validate()
    HybridSettings().validate()


def test_component_normalization_defines_edge_cases() -> None:
    assert normalize_component_scores({}) == {}
    assert normalize_component_scores({7: 4.0}) == {7: 1.0}
    assert normalize_component_scores({1: 2.0, 2: 2.0}) == {1: 1.0, 2: 1.0}
    assert normalize_component_scores({1: 0.0, 2: 0.0}) == {1: 0.0, 2: 0.0}
    assert normalize_component_scores({1: -3.0, 2: -1.0}) == {1: 0.0, 2: 1.0}


@pytest.mark.parametrize("value", [float("nan"), float("inf"), float("-inf")])
def test_component_normalization_rejects_non_finite_values(value: float) -> None:
    with pytest.raises(ValueError, match="finite"):
        normalize_component_scores({1: value})


def test_candidate_union_is_deterministic_deduplicated_and_capped() -> None:
    components = (
        {2: 0.5, 1: 1.0},
        {4: 0.5, 3: 1.0, 1: 0.25},
    )

    assert deterministic_candidate_union(components, pool_size=2) == (1, 3)
    assert deterministic_candidate_union(components, pool_size=10) == (1, 2, 3, 4)


def test_personalized_hybrid_scores_filters_and_selects_reason() -> None:
    component, als, availability = _hybrid_fixture()

    items = find_hybrid_personalized(
        component,
        als,
        _subject_id(),
        availability,
        10,
        exclude_previously_purchased=True,
    )

    assert items is not None
    assert [item.productId for item in items] == [_product_id(2), _product_id(3)]
    assert [item.finalScore for item in items] == [0.7, 0.3]
    assert items[0].reasonCode == "Hybrid.AlsDominant"
    assert items[1].reasonCode == "Hybrid.Association"
    assert all(item.productId != _product_id(1) for item in items)
    assert all(item.productId not in {_product_id(4), _product_id(5)} for item in items)


def test_similar_hybrid_scores_filters_source_and_selects_reason() -> None:
    component, als, availability = _hybrid_fixture()

    items = find_hybrid_similar(
        component,
        als.product_ids,
        availability,
        _product_id(1),
        10,
    )

    assert [item.productId for item in items] == [_product_id(2), _product_id(3)]
    assert [item.finalScore for item in items] == [0.7, 0.3]
    assert items[0].reasonCode == "Hybrid.Similar"
    assert items[1].reasonCode == "Hybrid.Association"
    assert all(item.productId != _product_id(1) for item in items)


def test_hybrid_ties_use_product_id_ascending() -> None:
    product_ids = tuple(_product_id(key) for key in range(1, 4))
    component = TrainedHybridComponent(
        popularity_scores=(0.0, 0.0, 0.0),
        association_neighbors=((), (), ()),
        content_neighbors=(
            (
                ScoredProductIndex(1, 0.5),
                ScoredProductIndex(2, 0.5),
            ),
            (),
            (),
        ),
        settings=HybridSettings(),
    )

    items = find_hybrid_similar(
        component,
        product_ids,
        (True, True, True),
        product_ids[0],
        10,
    )

    assert [item.productId for item in items] == [product_ids[1], product_ids[2]]
    assert items[0].finalScore == items[1].finalScore


def test_hybrid_request_falls_back_to_als_when_component_is_missing(
    tmp_path: Path,
    als_training_payload: dict[str, object],
) -> None:
    model = train_model(ModelTrainingRequest.model_validate(als_training_payload))
    service = RecommendationModelService(ArtifactStore(tmp_path))
    service._model = replace(model, hybrid=None)  # noqa: SLF001

    _, strategy, recommendations = service.personalized(
        _subject_id(),
        10,
        exclude_previously_purchased=True,
        strategy=PersonalizedModelStrategy.HYBRID,
    )

    assert strategy == "implicit_als"
    assert recommendations


def _hybrid_fixture() -> tuple[
    TrainedHybridComponent,
    TrainedAlsComponent,
    tuple[bool, ...],
]:
    product_ids = tuple(_product_id(key) for key in range(1, 6))
    component = TrainedHybridComponent(
        popularity_scores=(0.0, 0.2, 1.0, 1.0, 1.0),
        association_neighbors=(
            (
                ScoredProductIndex(2, 1.0),
                ScoredProductIndex(1, 0.1),
                ScoredProductIndex(3, 0.05),
            ),
            (),
            (),
            (),
            (),
        ),
        content_neighbors=(
            (
                ScoredProductIndex(3, 0.9),
                ScoredProductIndex(1, 0.8),
                ScoredProductIndex(2, 0.4),
            ),
            (),
            (),
            (),
            (),
        ),
        settings=HybridSettings(),
    )
    model = SimpleNamespace(
        user_factors=np.asarray([[1.0]], dtype=np.float32),
        item_factors=np.asarray(
            [[0.0], [0.8], [0.2], [1.0], [0.5]],
            dtype=np.float32,
        ),
    )
    als = TrainedAlsComponent(
        model=model,
        subject_ids=(_subject_id(),),
        product_ids=product_ids,
        purchased_product_indices=(frozenset({0}),),
    )
    return component, als, (True, True, True, False, False)


def _product_id(key: int) -> UUID:
    return UUID(f"00000000-0000-0000-0000-{key:012d}")


def _subject_id() -> str:
    return "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
