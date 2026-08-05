from __future__ import annotations

import os
import re
import tempfile
from pathlib import Path
from typing import Any
from uuid import UUID

import joblib
from scipy.sparse import csr_matrix
from sklearn.feature_extraction.text import TfidfVectorizer

from .contracts import ArtifactMetadata
from .model import ARTIFACT_SCHEMA_VERSION, TrainedModel

_SAFE_VERSION = re.compile(r"[^A-Za-z0-9._-]+")


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
        if value.get("artifactSchemaVersion") != ARTIFACT_SCHEMA_VERSION:
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
        if not isinstance(raw_product_ids, list) or not isinstance(
            availability, list
        ):
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
        return TrainedModel(
            metadata=metadata,
            vectorizer=vectorizer,
            matrix=matrix,
            product_ids=product_ids,
            availability=tuple(availability),
        )
