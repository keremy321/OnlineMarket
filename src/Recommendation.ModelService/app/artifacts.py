from __future__ import annotations

import os
import re
import tempfile
from pathlib import Path
from typing import Any
from uuid import UUID

import joblib
from implicit.cpu.als import AlternatingLeastSquares
from pydantic import TypeAdapter, ValidationError
from scipy.sparse import csr_matrix
from sklearn.feature_extraction.text import TfidfVectorizer

from .als import TrainedAlsComponent
from .contracts import ArtifactMetadata, SubjectId
from .model import (
    ARTIFACT_SCHEMA_VERSION,
    LEGACY_ARTIFACT_SCHEMA_VERSION,
    TrainedModel,
)

_SAFE_VERSION = re.compile(r"[^A-Za-z0-9._-]+")
_SUBJECT_ID_ADAPTER = TypeAdapter(SubjectId)


class ArtifactStore:
    def __init__(self, directory: Path) -> None:
        self._directory = directory

    def save(self, model: TrainedModel) -> Path:
        self._directory.mkdir(parents=True, exist_ok=True)
        timestamp = model.metadata.trainedAtUtc.strftime("%Y%m%dT%H%M%S%fZ")
        version = _SAFE_VERSION.sub("-", model.metadata.modelVersion)
        destination = self._directory / (
            f"model-{timestamp}-{version}-{model.metadata.inputHash[:12]}.joblib"
        )
        if destination.exists():
            raise FileExistsError("A model artifact with this identity exists.")
        temporary_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="wb",
                prefix=".model-",
                suffix=".tmp",
                dir=self._directory,
                delete=False,
            ) as temporary:
                temporary_path = Path(temporary.name)
                joblib.dump(model.to_artifact(), temporary)
                temporary.flush()
                os.fsync(temporary.fileno())
            self._from_artifact(joblib.load(temporary_path))
            os.replace(temporary_path, destination)
            return destination
        except Exception:
            if temporary_path is not None:
                temporary_path.unlink(missing_ok=True)
            raise

    def load_latest_valid(self) -> TrainedModel | None:
        if not self._directory.exists():
            return None
        candidates = sorted(
            self._directory.glob("model-*.joblib"),
            key=lambda path: path.name,
            reverse=True,
        )
        for candidate in candidates:
            try:
                return self._from_artifact(joblib.load(candidate))
            except Exception:
                continue
        return None

    @staticmethod
    def _from_artifact(value: Any) -> TrainedModel:
        if not isinstance(value, dict):
            raise ValueError("Artifact root must be a dictionary.")
        schema_version = value.get("artifactSchemaVersion")
        if schema_version not in (
            LEGACY_ARTIFACT_SCHEMA_VERSION,
            ARTIFACT_SCHEMA_VERSION,
        ):
            raise ValueError("Artifact schema version is unsupported.")
        metadata = ArtifactMetadata.model_validate(value.get("metadata"))
        vectorizer = value.get("vectorizer")
        matrix = value.get("matrix")
        raw_product_ids = value.get("productIds")
        availability = value.get("availability")
        if not isinstance(vectorizer, TfidfVectorizer):
            raise ValueError("Artifact vectorizer is invalid.")
        if not isinstance(matrix, csr_matrix):
            raise ValueError("Artifact matrix is invalid.")
        if not isinstance(raw_product_ids, list) or not isinstance(availability, list):
            raise ValueError("Artifact product state is invalid.")
        product_ids = tuple(UUID(item) for item in raw_product_ids)
        if (
            matrix.shape[0] != len(product_ids)
            or len(availability) != len(product_ids)
            or metadata.productCount != len(product_ids)
            or len(set(product_ids)) != len(product_ids)
            or any(not isinstance(item, bool) for item in availability)
        ):
            raise ValueError("Artifact dimensions are inconsistent.")
        als = (
            None
            if schema_version == LEGACY_ARTIFACT_SCHEMA_VERSION
            else ArtifactStore._read_als(value.get("als"), product_ids)
        )
        if (
            metadata.components is not None
            and metadata.components.als.status == "Succeeded"
            and als is None
        ):
            raise ValueError("Artifact ALS component is missing.")
        return TrainedModel(
            metadata=metadata,
            vectorizer=vectorizer,
            matrix=matrix,
            product_ids=product_ids,
            availability=tuple(availability),
            als=als,
        )

    @staticmethod
    def _read_als(
        value: Any,
        expected_product_ids: tuple[UUID, ...],
    ) -> TrainedAlsComponent | None:
        if value is None:
            return None
        if not isinstance(value, dict):
            raise ValueError("Artifact ALS root is invalid.")
        model = value.get("model")
        raw_subject_ids = value.get("subjectIds")
        raw_product_ids = value.get("productIds")
        raw_purchased = value.get("purchasedProductIndices")
        if (
            not isinstance(model, AlternatingLeastSquares)
            or not isinstance(raw_subject_ids, list)
            or not isinstance(raw_product_ids, list)
            or not isinstance(raw_purchased, list)
        ):
            raise ValueError("Artifact ALS state is invalid.")
        try:
            subject_ids = tuple(
                _SUBJECT_ID_ADAPTER.validate_python(item) for item in raw_subject_ids
            )
            product_ids = tuple(UUID(item) for item in raw_product_ids)
        except (TypeError, ValueError, ValidationError) as exception:
            raise ValueError("Artifact ALS mappings are invalid.") from exception
        if (
            len(subject_ids) != len(set(subject_ids))
            or product_ids != expected_product_ids
            or len(raw_purchased) != len(subject_ids)
            or model.user_factors.shape[0] != len(subject_ids)
            or model.item_factors.shape[0] != len(product_ids)
        ):
            raise ValueError("Artifact ALS dimensions are inconsistent.")
        purchased: list[frozenset[int]] = []
        for indices in raw_purchased:
            if (
                not isinstance(indices, list)
                or any(
                    not isinstance(index, int)
                    or isinstance(index, bool)
                    or index < 0
                    or index >= len(product_ids)
                    for index in indices
                )
                or len(indices) != len(set(indices))
            ):
                raise ValueError("Artifact purchase history is invalid.")
            purchased.append(frozenset(indices))
        return TrainedAlsComponent(
            model=model,
            subject_ids=subject_ids,
            product_ids=product_ids,
            purchased_product_indices=tuple(purchased),
        )
