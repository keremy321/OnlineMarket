from __future__ import annotations

import threading
from uuid import UUID

from .artifacts import ArtifactStore
from .contracts import (
    ArtifactMetadata,
    ModelTrainingRequest,
    SimilarProductItem,
)
from .model import TrainedModel, find_similar, train_model


class ModelUnavailableError(RuntimeError):
    pass


class TrainingAlreadyInProgressError(RuntimeError):
    pass


class RecommendationModelService:
    def __init__(self, artifact_store: ArtifactStore) -> None:
        self._artifact_store = artifact_store
        self._model: TrainedModel | None = None
        self._state_lock = threading.Lock()
        self._training_lock = threading.Lock()

    def load_latest_valid(self) -> bool:
        model = self._artifact_store.load_latest_valid()
        if model is None:
            return False
        with self._state_lock:
            self._model = model
        return True

    def train(self, request: ModelTrainingRequest) -> ArtifactMetadata:
        if not self._training_lock.acquire(blocking=False):
            raise TrainingAlreadyInProgressError
        try:
            candidate = train_model(request)
            self._artifact_store.save(candidate)
            with self._state_lock:
                self._model = candidate
            return candidate.metadata
        finally:
            self._training_lock.release()

    def current(self) -> ArtifactMetadata:
        with self._state_lock:
            model = self._model
        if model is None:
            raise ModelUnavailableError
        return model.metadata

    def similar(
        self,
        product_id: UUID,
        limit: int,
    ) -> tuple[str, list[SimilarProductItem]]:
        with self._state_lock:
            model = self._model
        if model is None:
            raise ModelUnavailableError
        return (
            model.metadata.modelVersion,
            find_similar(model, product_id, limit),
        )
