from __future__ import annotations

import logging
import secrets
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, Request, status
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import JSONResponse

from .artifacts import ArtifactStore
from .config import Settings
from .contracts import (
    ApiErrorResponse,
    ArtifactMetadata,
    ModelEvaluationRequest,
    ModelEvaluationResponse,
    ModelTrainingRequest,
    ModelTrainingResponse,
    PersonalizedRecommendationsRequest,
    PersonalizedRecommendationsResponse,
    SimilarProductsRequest,
    SimilarProductsResponse,
)
from .service import (
    EvaluationAlreadyInProgressError,
    ModelUnavailableError,
    RecommendationModelService,
    TrainingAlreadyInProgressError,
)

LOGGER = logging.getLogger("recommendation_model_service")


def create_app(settings: Settings | None = None) -> FastAPI:
    resolved_settings = settings or Settings.from_environment()
    resolved_settings.validate()
    model_service = RecommendationModelService(
        ArtifactStore(resolved_settings.artifact_directory),
        als_settings=resolved_settings.als,
        hybrid_settings=resolved_settings.hybrid,
        evaluation_settings=resolved_settings.evaluation,
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        loaded = await run_in_threadpool(model_service.load_latest_valid)
        LOGGER.info("Model service startup completed; model_loaded=%s.", loaded)
        yield

    application = FastAPI(
        title="Recommendation.ModelService",
        version="1.0.0",
        docs_url=None,
        redoc_url=None,
        lifespan=lifespan,
    )
    application.state.model_service = model_service

    def require_api_key(
        x_api_key: str | None = Header(default=None, alias="X-Api-Key"),
    ) -> None:
        configured = resolved_settings.api_key
        if not configured:
            raise AuthenticationConfigurationError
        if x_api_key is None or not secrets.compare_digest(
            x_api_key.encode("utf-8"),
            configured.encode("utf-8"),
        ):
            raise AuthenticationError

    @application.exception_handler(AuthenticationError)
    async def authentication_error_handler(
        _: Request,
        __: AuthenticationError,
    ) -> JSONResponse:
        return error_response(
            status.HTTP_401_UNAUTHORIZED,
            "Authentication.ApiKeyInvalid",
            "A valid API key is required.",
            False,
        )

    @application.exception_handler(AuthenticationConfigurationError)
    async def authentication_configuration_handler(
        _: Request,
        __: AuthenticationConfigurationError,
    ) -> JSONResponse:
        return error_response(
            status.HTTP_503_SERVICE_UNAVAILABLE,
            "Authentication.ConfigurationMissing",
            "API-key authentication is unavailable.",
            True,
        )

    @application.exception_handler(ModelUnavailableError)
    async def model_unavailable_handler(
        _: Request,
        __: ModelUnavailableError,
    ) -> JSONResponse:
        return error_response(
            status.HTTP_503_SERVICE_UNAVAILABLE,
            "RecommendationModel.Unavailable",
            "No valid recommendation model is available.",
            True,
        )

    @application.exception_handler(TrainingAlreadyInProgressError)
    async def training_in_progress_handler(
        _: Request,
        __: TrainingAlreadyInProgressError,
    ) -> JSONResponse:
        return error_response(
            status.HTTP_409_CONFLICT,
            "RecommendationModel.TrainingAlreadyInProgress",
            "Model training is already in progress.",
            False,
        )

    @application.exception_handler(EvaluationAlreadyInProgressError)
    async def evaluation_in_progress_handler(
        _: Request,
        __: EvaluationAlreadyInProgressError,
    ) -> JSONResponse:
        return error_response(
            status.HTTP_409_CONFLICT,
            "RecommendationModel.EvaluationAlreadyInProgress",
            "Model evaluation is already in progress.",
            False,
        )

    @application.get("/health", include_in_schema=False)
    async def health() -> dict[str, str]:
        return {"status": "healthy"}

    @application.post(
        "/api/v1/models/train",
        response_model=ModelTrainingResponse,
        dependencies=[Depends(require_api_key)],
    )
    async def train(request: ModelTrainingRequest) -> ModelTrainingResponse:
        metadata = await run_in_threadpool(model_service.train, request)
        LOGGER.info(
            "Model training completed; model_version=%s product_count=%s "
            "subject_count=%s interaction_count=%s tfidf_status=%s "
            "als_status=%s correlation_id=%s.",
            metadata.modelVersion,
            metadata.productCount,
            metadata.subjectCount,
            metadata.interactionCount,
            metadata.components.tfidf.status if metadata.components else None,
            metadata.components.als.status if metadata.components else None,
            metadata.correlationId,
        )
        return ModelTrainingResponse(status="Succeeded", metadata=metadata)

    @application.post(
        "/api/v1/models/evaluate",
        response_model=ModelEvaluationResponse,
        dependencies=[Depends(require_api_key)],
    )
    async def evaluate(request: ModelEvaluationRequest) -> ModelEvaluationResponse:
        response = await run_in_threadpool(model_service.evaluate, request)
        LOGGER.info(
            "Model evaluation completed; evaluation_version=%s product_count=%s "
            "subject_count=%s order_count=%s interaction_count=%s "
            "eligible_subject_count=%s popularity_status=%s als_status=%s "
            "report_identifier=%s.",
            response.evaluationVersion,
            response.dataset.productCount,
            response.dataset.subjectCount,
            response.dataset.orderCount,
            response.dataset.interactionCount,
            response.split.eligibleSubjectCount,
            response.models.popularity.status,
            response.models.als.status,
            response.reportIdentifier,
        )
        return response

    @application.post(
        "/api/v1/models/similar",
        response_model=SimilarProductsResponse,
        dependencies=[Depends(require_api_key)],
    )
    async def similar(
        request: SimilarProductsRequest,
    ) -> SimilarProductsResponse:
        model_version, strategy, items = await run_in_threadpool(
            model_service.similar,
            request.productId,
            request.limit,
        )
        return SimilarProductsResponse(
            modelVersion=model_version,
            strategy=strategy,
            sourceProductId=request.productId,
            items=items,
        )

    @application.post(
        "/api/v1/models/personalized",
        response_model=PersonalizedRecommendationsResponse,
        dependencies=[Depends(require_api_key)],
    )
    async def personalized(
        request: PersonalizedRecommendationsRequest,
    ) -> PersonalizedRecommendationsResponse:
        model_version, strategy, recommendations = await run_in_threadpool(
            model_service.personalized,
            request.subjectId,
            request.limit,
            exclude_previously_purchased=(request.excludePreviouslyPurchased),
            strategy=request.strategy,
        )
        return PersonalizedRecommendationsResponse(
            modelVersion=model_version,
            strategy=strategy,
            recommendations=recommendations,
        )

    @application.get(
        "/api/v1/models/current",
        response_model=ArtifactMetadata,
        dependencies=[Depends(require_api_key)],
    )
    async def current() -> ArtifactMetadata:
        return model_service.current()

    return application


class AuthenticationError(Exception):
    pass


class AuthenticationConfigurationError(Exception):
    pass


def error_response(
    status_code: int,
    code: str,
    message: str,
    retryable: bool,
) -> JSONResponse:
    body = ApiErrorResponse(
        code=code,
        message=message,
        retryable=retryable,
    )
    return JSONResponse(status_code=status_code, content=body.model_dump())


app = create_app()
