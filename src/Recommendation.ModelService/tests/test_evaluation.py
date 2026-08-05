from __future__ import annotations

import math
import os
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID

import pytest

from app.artifacts import ArtifactStore
from app.config import AlsSettings, EvaluationSettings
from app.contracts import (
    EvaluationModelMetrics,
    ModelEvaluationRequest,
    ModelTrainingRequest,
)
from app.evaluation import (
    EvaluationReportWriter,
    build_temporal_split,
    evaluate_request,
    p95_latency,
    ranking_metrics,
)
from app.service import RecommendationModelService
from tests.conftest import evaluation_interaction


def test_chronological_split_uses_timestamp_and_stable_order_id_tie_breaker() -> None:
    subject = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    payload = {
        "evaluationVersion": "tie-v1",
        "catalogueProductIds": [_product_id(1), _product_id(2)],
        "candidateProductIds": [_product_id(1), _product_id(2)],
        "interactions": [
            evaluation_interaction(
                2, subject, "2026-01-02T00:00:00Z", [(2, 1)]
            ),
            evaluation_interaction(
                1, subject, "2026-01-02T00:00:00Z", [(1, 1)]
            ),
            evaluation_interaction(
                3, subject, "2026-01-01T00:00:00Z", [(1, 1)]
            ),
        ],
    }

    split = build_temporal_split(
        ModelEvaluationRequest.model_validate(payload),
        EvaluationSettings(),
    )

    assert [order.order_id for order in split.training_orders] == [
        _order_id(3),
        _order_id(1),
    ]
    assert [order.order_id for order in split.test_orders] == [_order_id(2)]


def test_split_prevents_future_interaction_leakage(
    evaluation_payload: dict[str, object],
) -> None:
    request = ModelEvaluationRequest.model_validate(evaluation_payload)

    split = build_temporal_split(request, EvaluationSettings())

    for subject_id in split.eligible_subject_ids:
        training = [
            order for order in split.training_orders if order.subject_id == subject_id
        ]
        tests = [order for order in split.test_orders if order.subject_id == subject_id]
        assert max(
            (order.occurred_at_utc, order.order_id.hex) for order in training
        ) < min((order.occurred_at_utc, order.order_id.hex) for order in tests)


def test_insufficient_history_and_unknown_products_are_reported() -> None:
    subject_a = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    subject_b = "v1.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
    payload = {
        "evaluationVersion": "excluded-v1",
        "catalogueProductIds": [_product_id(1), _product_id(2)],
        "candidateProductIds": [_product_id(1), _product_id(2)],
        "interactions": [
            evaluation_interaction(
                1, subject_a, "2026-01-01T00:00:00Z", [(1, 1), (99, 1)]
            ),
            evaluation_interaction(
                2, subject_a, "2026-01-02T00:00:00Z", [(2, 1)]
            ),
            evaluation_interaction(
                3, subject_b, "2026-01-01T00:00:00Z", [(1, 1)]
            ),
        ],
    }

    split = build_temporal_split(
        ModelEvaluationRequest.model_validate(payload),
        EvaluationSettings(),
    )

    assert split.eligible_subject_ids == (subject_a,)
    assert split.insufficient_history_subject_count == 1
    assert split.unknown_product_interaction_count == 1
    assert all(
        item.productId != UUID(_product_id(99))
        for order in split.training_orders
        for item in order.items
    )


def test_required_ranking_metrics_and_p95_are_calculated() -> None:
    subject = "subject"
    first = UUID(_product_id(1))
    second = UUID(_product_id(2))
    third = UUID(_product_id(3))
    fourth = UUID(_product_id(4))

    precision, recall, hit_rate, ndcg = ranking_metrics(
        {subject: (first, second, third)},
        {subject: frozenset((second, fourth))},
        k=5,
    )

    expected_dcg = 1 / math.log2(3)
    expected_ideal = 1 + 1 / math.log2(3)
    assert precision == pytest.approx(0.2)
    assert recall == pytest.approx(0.5)
    assert hit_rate == 1
    assert ndcg == pytest.approx(expected_dcg / expected_ideal)
    assert p95_latency([float(value) for value in range(1, 21)]) == 19


def test_popularity_and_als_are_evaluated_with_catalogue_coverage(
    evaluation_payload: dict[str, object],
) -> None:
    response = evaluate_request(
        ModelEvaluationRequest.model_validate(evaluation_payload),
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(),
        evaluated_at_utc=datetime(2026, 1, 5, tzinfo=UTC),
    )

    assert response.models.popularity.status == "Evaluated"
    assert response.models.als.status == "Evaluated"
    assert response.models.tfidf.status == "NotEvaluated"
    assert response.models.fbt.status == "NotEvaluated"
    assert response.models.popularity.metrics is not None
    assert response.models.als.metrics is not None
    assert 0 <= response.models.popularity.metrics.catalogueCoverage <= 1
    assert 0 <= response.models.als.metrics.catalogueCoverage <= 1
    assert response.split.trainingInteractionCount == 3
    assert response.split.testInteractionCount == 3


def test_empty_dataset_and_no_eligible_subjects_are_safe() -> None:
    empty = ModelEvaluationRequest.model_validate(
        {
            "evaluationVersion": "empty-v1",
            "catalogueProductIds": [],
            "candidateProductIds": [],
            "interactions": [],
        }
    )

    response = evaluate_request(
        empty,
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(),
    )

    assert response.split.eligibleSubjectCount == 0
    assert response.models.popularity.status == "NotEvaluated"
    assert response.models.als.status == "NotEvaluated"


def test_repeated_evaluation_has_stable_hash_and_ranking_metrics(
    evaluation_payload: dict[str, object],
) -> None:
    request = ModelEvaluationRequest.model_validate(evaluation_payload)

    first = evaluate_request(
        request,
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(),
    )
    second = evaluate_request(
        request,
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(),
    )

    assert first.inputHash == second.inputHash
    assert _ranking_signature(first.models.popularity.metrics) == _ranking_signature(
        second.models.popularity.metrics
    )
    assert _ranking_signature(first.models.als.metrics) == _ranking_signature(
        second.models.als.metrics
    )


def test_evaluation_does_not_change_active_artifact(
    tmp_path: Path,
    training_payload: dict[str, object],
    evaluation_payload: dict[str, object],
) -> None:
    artifact_directory = tmp_path / "artifacts"
    service = RecommendationModelService(
        ArtifactStore(artifact_directory),
        AlsSettings(),
        EvaluationSettings(output_directory=tmp_path / "reports"),
    )
    metadata = service.train(ModelTrainingRequest.model_validate(training_payload))
    before = sorted(path.name for path in artifact_directory.glob("*.joblib"))

    service.evaluate(ModelEvaluationRequest.model_validate(evaluation_payload))

    assert service.current().modelVersion == metadata.modelVersion
    assert sorted(path.name for path in artifact_directory.glob("*.joblib")) == before


def test_reports_publish_atomically_and_contain_no_identifiers(
    tmp_path: Path,
    evaluation_payload: dict[str, object],
) -> None:
    request = ModelEvaluationRequest.model_validate(evaluation_payload)
    response = evaluate_request(
        request,
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(output_directory=tmp_path),
    )
    subjects = tuple(interaction.subjectId for interaction in request.interactions)

    EvaluationReportWriter(tmp_path).publish(response, subject_ids=subjects)

    json_content = (tmp_path / response.reports.jsonFile).read_text(encoding="utf-8")
    markdown_content = (tmp_path / response.reports.markdownFile).read_text(
        encoding="utf-8"
    )
    combined = json_content + markdown_content
    assert all(subject not in combined for subject in subjects)
    assert "customerId" not in combined
    assert "subjectId" not in combined
    assert "Precision@5" in markdown_content
    assert "runtime-dependent" in markdown_content


def test_report_publication_rolls_back_when_second_publish_fails(
    tmp_path: Path,
    evaluation_payload: dict[str, object],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    request = ModelEvaluationRequest.model_validate(evaluation_payload)
    response = evaluate_request(
        request,
        als_settings=AlsSettings(),
        evaluation_settings=EvaluationSettings(output_directory=tmp_path),
    )
    original_replace = os.replace
    calls = 0

    def fail_second_replace(source: str | Path, destination: str | Path) -> None:
        nonlocal calls
        calls += 1
        if calls == 2:
            raise OSError("simulated report publication failure")
        original_replace(source, destination)

    monkeypatch.setattr(os, "replace", fail_second_replace)

    with pytest.raises(OSError):
        EvaluationReportWriter(tmp_path).publish(
            response,
            subject_ids=tuple(
                interaction.subjectId for interaction in request.interactions
            ),
        )

    assert not (tmp_path / response.reports.jsonFile).exists()
    assert not (tmp_path / response.reports.markdownFile).exists()


def _ranking_signature(
    metrics: EvaluationModelMetrics | None,
) -> tuple[float, ...]:
    assert metrics is not None
    return (
        metrics.precisionAtK,
        metrics.recallAtK,
        metrics.hitRateAtK,
        metrics.ndcgAtK,
        metrics.catalogueCoverage,
        metrics.knownSubjectCatalogueCoverage,
        metrics.catalogueCoverageIncludingFallback,
    )


def _product_id(key: int) -> str:
    return f"00000000-0000-0000-0000-{key:012d}"


def _order_id(key: int) -> UUID:
    return UUID(f"40000000-0000-0000-0000-{key:012d}")
