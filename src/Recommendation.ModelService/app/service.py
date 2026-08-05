from __future__ import annotations

import threading
from uuid import UUID

from .als import (
    COLD_START_STRATEGY,
    PERSONALIZED_STRATEGY,
    find_personalized,
)
from .artifacts import ArtifactStore
from .config import AlsSettings, EvaluationSettings
from .contracts import (
    ArtifactMetadata,
    ModelEvaluationRequest,
    ModelEvaluationResponse,
    ModelTrainingRequest,
    PersonalizedRecommendationItem,
    SimilarProductItem,
)
from .evaluation import EvaluationReportWriter, evaluate_request
from .model import TrainedModel, find_similar, train_model


class ModelUnavailableError(RuntimeError):
    pass


class TrainingAlreadyInProgressError(RuntimeError):
    pass


class EvaluationAlreadyInProgressError(RuntimeError):
    pass


class RecommendationModelService:
    def __init__(
        self,
        artifact_store: ArtifactStore,
        als_settings: AlsSettings | None = None,
        evaluation_settings: EvaluationSettings | None = None,
    ) -> None:
        self._artifact_store = artifact_store
        self._als_settings = als_settings or AlsSettings()
        self._evaluation_settings = evaluation_settings or EvaluationSettings()
        self._evaluation_report_writer = EvaluationReportWriter(
            self._evaluation_settings.output_directory
        )
        self._model: TrainedModel | None = None
        self._state_lock = threading.Lock()
        self._training_lock = threading.Lock()
        self._evaluation_lock = threading.Lock()

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
            candidate = train_model(
                request,
                als_settings=self._als_settings,
            )
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

    def personalized(
        self,
        subject_id: str,
        limit: int | None,
        *,
        exclude_previously_purchased: bool,
    ) -> tuple[str, str, list[PersonalizedRecommendationItem]]:
        with self._state_lock:
            model = self._model
        if model is None:
            raise ModelUnavailableError
        resolved_limit = min(
            limit or self._als_settings.default_limit,
            self._als_settings.maximum_limit,
        )
        recommendations = find_personalized(
            model.als,
            subject_id,
            model.availability,
            resolved_limit,
            exclude_previously_purchased=exclude_previously_purchased,
        )
        if recommendations is None:
            return model.metadata.modelVersion, COLD_START_STRATEGY, []
        return (
            model.metadata.modelVersion,
            PERSONALIZED_STRATEGY,
            recommendations,
        )

    def evaluate(self, request: ModelEvaluationRequest) -> ModelEvaluationResponse:
        if not self._evaluation_lock.acquire(blocking=False):
            raise EvaluationAlreadyInProgressError
        try:
            response = evaluate_request(
                request,
                als_settings=self._als_settings,
                evaluation_settings=self._evaluation_settings,
            )
            self._evaluation_report_writer.publish(
                response,
                subject_ids=tuple(
                    sorted(
                        {
                            interaction.subjectId
                            for interaction in request.interactions
                        }
                    )
                ),
            )
            return response
        finally:
            self._evaluation_lock.release()
