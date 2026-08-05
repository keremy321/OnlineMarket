from __future__ import annotations

from copy import deepcopy
from datetime import UTC, datetime, timedelta
from pathlib import Path
from unittest.mock import Mock

import joblib
import pytest
from conftest import SOURCE_ID

from app.als import find_personalized
from app.artifacts import ArtifactStore
from app.contracts import ModelTrainingRequest
from app.model import find_similar, train_model
from app.service import RecommendationModelService


def test_artifact_round_trip(
    tmp_path: Path,
    training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(training_payload)
    model = train_model(
        request,
        trained_at_utc=datetime(2026, 8, 5, 10, 0, tzinfo=UTC),
    )
    store = ArtifactStore(tmp_path)

    path = store.save(model)
    loaded = store.load_latest_valid()

    assert path.exists()
    assert loaded is not None
    assert loaded.metadata == model.metadata
    assert find_similar(loaded, SOURCE_ID, 10) == find_similar(model, SOURCE_ID, 10)


def test_corrupted_latest_artifact_falls_back_to_previous_valid(
    tmp_path: Path,
    training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(training_payload)
    store = ArtifactStore(tmp_path)
    valid = train_model(
        request,
        trained_at_utc=datetime(2026, 8, 5, 10, 0, tzinfo=UTC),
    )
    store.save(valid)
    (tmp_path / "model-99999999T999999999999Z-corrupt.joblib").write_bytes(
        b"not-a-joblib-artifact"
    )

    loaded = store.load_latest_valid()

    assert loaded is not None
    assert loaded.metadata.modelVersion == request.modelVersion


def test_failed_training_preserves_previous_in_memory_model(
    tmp_path: Path,
    training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(training_payload)
    real_store = ArtifactStore(tmp_path)
    service = RecommendationModelService(real_store)
    previous = service.train(request)
    failing_store = Mock(spec=ArtifactStore)
    failing_store.save.side_effect = OSError("injected artifact failure")
    failing_service = RecommendationModelService(failing_store)
    failing_service._model = real_store.load_latest_valid()  # noqa: SLF001
    replacement_payload = deepcopy(training_payload)
    replacement_payload["modelVersion"] = "tfidf-test-v2"
    replacement = ModelTrainingRequest.model_validate(replacement_payload)

    with pytest.raises(OSError, match="injected artifact failure"):
        failing_service.train(replacement)

    assert failing_service.current().modelVersion == previous.modelVersion


def test_latest_valid_artifact_is_selected_by_versioned_timestamp(
    tmp_path: Path,
    training_payload: dict[str, object],
) -> None:
    base = ModelTrainingRequest.model_validate(training_payload)
    newer_payload = deepcopy(training_payload)
    newer_payload["modelVersion"] = "tfidf-test-v2"
    newer = ModelTrainingRequest.model_validate(newer_payload)
    store = ArtifactStore(tmp_path)
    store.save(
        train_model(
            base,
            trained_at_utc=datetime(2026, 8, 5, 10, 0, tzinfo=UTC),
        )
    )
    store.save(
        train_model(
            newer,
            trained_at_utc=datetime(2026, 8, 5, 10, 0, tzinfo=UTC)
            + timedelta(seconds=1),
        )
    )

    loaded = store.load_latest_valid()

    assert loaded is not None
    assert loaded.metadata.modelVersion == "tfidf-test-v2"


def test_als_artifact_round_trip_preserves_personalized_inference(
    tmp_path: Path,
    als_training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(als_training_payload)
    model = train_model(
        request,
        trained_at_utc=datetime(2026, 8, 5, 11, 0, tzinfo=UTC),
    )
    store = ArtifactStore(tmp_path)

    store.save(model)
    loaded = store.load_latest_valid()

    assert loaded is not None
    assert loaded.als is not None
    subject_id = request.interactions[0].subjectId
    assert find_personalized(
        loaded.als,
        subject_id,
        loaded.availability,
        10,
        exclude_previously_purchased=True,
    ) == find_personalized(
        model.als,
        subject_id,
        model.availability,
        10,
        exclude_previously_purchased=True,
    )


def test_legacy_tfidf_only_artifact_remains_loadable(
    tmp_path: Path,
    training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(training_payload)
    model = train_model(request)
    value = model.to_artifact()
    value["artifactSchemaVersion"] = 1
    value.pop("als")
    metadata = value["metadata"]
    assert isinstance(metadata, dict)
    for field in (
        "subjectCount",
        "interactionCount",
        "algorithmComponents",
        "components",
        "alsParameters",
    ):
        metadata.pop(field)
    path = tmp_path / "model-20200101T000000000000Z-legacy.joblib"
    joblib.dump(value, path)

    loaded = ArtifactStore(tmp_path).load_latest_valid()

    assert loaded is not None
    assert loaded.als is None
    assert find_similar(loaded, SOURCE_ID, 10)


def test_corrupted_als_component_falls_back_to_previous_valid_artifact(
    tmp_path: Path,
    als_training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(als_training_payload)
    store = ArtifactStore(tmp_path)
    valid = train_model(
        request,
        trained_at_utc=datetime(2026, 8, 5, 12, 0, tzinfo=UTC),
    )
    store.save(valid)
    corrupted = valid.to_artifact()
    als = corrupted["als"]
    assert isinstance(als, dict)
    als["subjectIds"] = ["direct-customer-id"]
    joblib.dump(
        corrupted,
        tmp_path / "model-99999999T999999999999Z-corrupt-als.joblib",
    )

    loaded = store.load_latest_valid()

    assert loaded is not None
    assert loaded.metadata.modelVersion == valid.metadata.modelVersion
    assert loaded.als is not None


def test_failed_als_training_preserves_previous_active_model(
    tmp_path: Path,
    als_training_payload: dict[str, object],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    request = ModelTrainingRequest.model_validate(als_training_payload)
    service = RecommendationModelService(ArtifactStore(tmp_path))
    previous = service.train(request)

    def fail_als(*_: object, **__: object) -> object:
        raise RuntimeError("injected ALS failure")

    monkeypatch.setattr("app.model.train_als_component", fail_als)
    replacement_payload = deepcopy(als_training_payload)
    replacement_payload["modelVersion"] = "model-set-test-v2"
    replacement = ModelTrainingRequest.model_validate(replacement_payload)

    with pytest.raises(RuntimeError, match="injected ALS failure"):
        service.train(replacement)

    assert service.current().modelVersion == previous.modelVersion


def test_artifact_contains_no_direct_customer_identifier_field(
    als_training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(als_training_payload)
    artifact = train_model(request).to_artifact()

    assert "customerid" not in repr(artifact).lower()
