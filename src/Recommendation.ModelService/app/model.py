from __future__ import annotations

import hashlib
import json
import platform
from dataclasses import dataclass
from datetime import UTC, datetime
from time import perf_counter
from typing import Any
from uuid import UUID

import fastapi
import implicit
import joblib
import numpy as np
import pydantic
import scipy
import sklearn
from scipy.sparse import csr_matrix
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity

from .als import ALGORITHM as ALS_ALGORITHM
from .als import TrainedAlsComponent, train_als_component
from .config import AlsSettings, HybridSettings
from .contracts import (
    AlsParameters,
    ArtifactMetadata,
    HybridParameters,
    ModelComponentState,
    ModelComponentStatus,
    ModelComponentStatuses,
    ModelTrainingRequest,
    OrderProductInteraction,
    PersonalizedHybridWeightsParameters,
    SimilarHybridWeightsParameters,
    SimilarProductItem,
)
from .features import build_product_document
from .hybrid import (
    ALGORITHM as HYBRID_ALGORITHM,
)
from .hybrid import (
    ASSOCIATION_ALGORITHM,
    POPULARITY_ALGORITHM,
    SIMILAR_STRATEGY,
    ScoredProductIndex,
    TrainedHybridComponent,
    build_hybrid_component,
    find_hybrid_similar,
)

TFIDF_ALGORITHM = "tfidf-product-content-cosine-v1"
ARTIFACT_SCHEMA_VERSION = 3
ALS_ARTIFACT_SCHEMA_VERSION = 2
LEGACY_ARTIFACT_SCHEMA_VERSION = 1
TFIDF_STRATEGY = "PythonTfidf"


@dataclass(frozen=True, slots=True)
class TrainedModel:
    metadata: ArtifactMetadata
    vectorizer: TfidfVectorizer
    matrix: csr_matrix
    product_ids: tuple[UUID, ...]
    availability: tuple[bool, ...]
    als: TrainedAlsComponent | None = None
    hybrid: TrainedHybridComponent | None = None

    def to_artifact(self) -> dict[str, Any]:
        return {
            "artifactSchemaVersion": ARTIFACT_SCHEMA_VERSION,
            "metadata": self.metadata.model_dump(mode="json"),
            "vectorizer": self.vectorizer,
            "matrix": self.matrix,
            "productIds": [str(product_id) for product_id in self.product_ids],
            "availability": list(self.availability),
            "als": None
            if self.als is None
            else {
                "model": self.als.model,
                "subjectIds": list(self.als.subject_ids),
                "productIds": [str(product_id) for product_id in self.als.product_ids],
                "purchasedProductIndices": [
                    sorted(indices) for indices in self.als.purchased_product_indices
                ],
            },
            "hybrid": None
            if self.hybrid is None
            else {
                "popularityScores": list(self.hybrid.popularity_scores),
                "associationNeighbors": _serialize_neighbors(
                    self.hybrid.association_neighbors
                ),
                "contentNeighbors": _serialize_neighbors(
                    self.hybrid.content_neighbors
                ),
                "settings": hybrid_parameters(
                    self.hybrid.settings
                ).model_dump(mode="json"),
            },
        }


def calculate_input_hash(request: ModelTrainingRequest) -> str:
    products: list[dict[str, Any]] = sorted(
        (product.model_dump(mode="json") for product in request.products),
        key=lambda item: str(item["productId"]),
    )
    interactions: list[dict[str, Any]] = sorted(
        (interaction_payload(item) for item in request.interactions),
        key=lambda item: str(item["orderId"]),
    )
    canonical = json.dumps(
        {"products": products, "interactions": interactions},
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def interaction_payload(
    interaction: OrderProductInteraction,
) -> dict[str, Any]:
    items: list[dict[str, Any]] = [
        item.model_dump(mode="json") for item in interaction.items
    ]
    items.sort(key=lambda item: str(item["productId"]))
    return {
        "orderId": str(interaction.orderId),
        "subjectId": interaction.subjectId,
        "items": items,
    }


def train_model(
    request: ModelTrainingRequest,
    *,
    als_settings: AlsSettings | None = None,
    hybrid_settings: HybridSettings | None = None,
    trained_at_utc: datetime | None = None,
) -> TrainedModel:
    resolved_als_settings = als_settings or AlsSettings()
    resolved_hybrid_settings = hybrid_settings or HybridSettings()
    resolved_als_settings.validate()
    resolved_hybrid_settings.validate()
    ordered_products = sorted(
        request.products,
        key=lambda product: product.productId.hex,
    )
    product_ids = tuple(product.productId for product in ordered_products)
    if len(set(product_ids)) != len(product_ids):
        raise ValueError("Training products must have unique productId values.")

    tfidf_started = perf_counter()
    documents = [build_product_document(product) for product in ordered_products]
    vectorizer = TfidfVectorizer(
        lowercase=False,
        norm="l2",
        token_pattern=r"(?u)\b\w\w+\b",
        dtype=np.float64,
    )
    matrix = vectorizer.fit_transform(documents).tocsr()
    tfidf_duration = _duration_milliseconds(tfidf_started)
    als_started = perf_counter()
    als, _ = train_als_component(
        request.interactions,
        product_ids,
        resolved_als_settings,
    )
    als_duration = _duration_milliseconds(als_started)
    hybrid_started = perf_counter()
    hybrid = build_hybrid_component(
        product_ids,
        matrix,
        request.interactions,
        resolved_hybrid_settings,
    )
    hybrid_duration = _duration_milliseconds(hybrid_started)
    subject_count = len({interaction.subjectId for interaction in request.interactions})
    interaction_count = sum(
        len(interaction.items) for interaction in request.interactions
    )
    trained_at = trained_at_utc or datetime.now(UTC)
    algorithm_components = [TFIDF_ALGORITHM]
    if als is not None:
        algorithm_components.append(ALS_ALGORITHM)
    algorithm_components.extend(
        [POPULARITY_ALGORITHM, ASSOCIATION_ALGORITHM, HYBRID_ALGORITHM]
    )
    metadata = ArtifactMetadata(
        modelVersion=request.modelVersion,
        correlationId=request.correlationId,
        trainedAtUtc=trained_at,
        productCount=len(ordered_products),
        subjectCount=subject_count,
        interactionCount=interaction_count,
        inputHash=calculate_input_hash(request),
        algorithm="+".join(algorithm_components),
        algorithmComponents=algorithm_components,
        components=ModelComponentStatuses(
            tfidf=ModelComponentStatus(
                status=ModelComponentState.SUCCEEDED,
                trainingDurationMilliseconds=tfidf_duration,
            ),
            als=ModelComponentStatus(
                status=(
                    ModelComponentState.SUCCEEDED
                    if als is not None
                    else ModelComponentState.INSUFFICIENT_DATA
                ),
                trainingDurationMilliseconds=als_duration,
            ),
            popularity=ModelComponentStatus(
                status=ModelComponentState.SUCCEEDED,
                trainingDurationMilliseconds=hybrid_duration,
            ),
            association=ModelComponentStatus(
                status=ModelComponentState.SUCCEEDED,
                trainingDurationMilliseconds=hybrid_duration,
            ),
            hybrid=ModelComponentStatus(
                status=ModelComponentState.SUCCEEDED,
                trainingDurationMilliseconds=hybrid_duration,
            ),
        ),
        alsParameters=AlsParameters(
            factors=resolved_als_settings.factors,
            regularization=resolved_als_settings.regularization,
            iterations=resolved_als_settings.iterations,
            alpha=resolved_als_settings.alpha,
            randomSeed=resolved_als_settings.random_seed,
        ),
        hybridParameters=hybrid_parameters(resolved_hybrid_settings),
        libraryVersions={
            "python": platform.python_version(),
            "fastapi": fastapi.__version__,
            "pydantic": pydantic.__version__,
            "scikit-learn": sklearn.__version__,
            "joblib": joblib.__version__,
            "numpy": np.__version__,
            "scipy": scipy.__version__,
            "implicit": implicit.__version__,
        },
    )
    return TrainedModel(
        metadata=metadata,
        vectorizer=vectorizer,
        matrix=matrix,
        product_ids=product_ids,
        availability=tuple(
            product.isActive and product.isInStock for product in ordered_products
        ),
        als=als,
        hybrid=hybrid,
    )


def _duration_milliseconds(started: float) -> int:
    return max(0, round((perf_counter() - started) * 1_000))


def find_similar(
    model: TrainedModel,
    source_product_id: UUID,
    limit: int,
) -> list[SimilarProductItem]:
    return find_similar_with_strategy(model, source_product_id, limit)[1]


def find_similar_with_strategy(
    model: TrainedModel,
    source_product_id: UUID,
    limit: int,
) -> tuple[str, list[SimilarProductItem]]:
    if model.hybrid is not None:
        return (
            SIMILAR_STRATEGY,
            find_hybrid_similar(
                model.hybrid,
                model.product_ids,
                model.availability,
                source_product_id,
                limit,
            ),
        )
    try:
        source_index = model.product_ids.index(source_product_id)
    except ValueError:
        return TFIDF_STRATEGY, []

    similarities = cosine_similarity(
        model.matrix[source_index],
        model.matrix,
    ).ravel()
    candidates = [
        SimilarProductItem(
            productId=product_id,
            tfidfScore=round(float(np.clip(similarities[index], 0, 1)), 12),
            coPurchaseScore=0.0,
            popularityScore=0.0,
            finalScore=round(float(np.clip(similarities[index], 0, 1)), 12),
            reasonCode="Similarity.TfidfContent",
            reasonText="Similar product based on content features.",
        )
        for index, product_id in enumerate(model.product_ids)
        if index != source_index and model.availability[index]
    ]
    candidates.sort(key=lambda item: (-item.tfidfScore, item.productId.hex))
    return TFIDF_STRATEGY, candidates[:limit]


def hybrid_parameters(settings: HybridSettings) -> HybridParameters:
    return HybridParameters(
        personalizedWeights=PersonalizedHybridWeightsParameters(
            als=settings.personalized.als,
            contentAffinity=settings.personalized.content,
            association=settings.personalized.association,
            popularity=settings.personalized.popularity,
        ),
        similarWeights=SimilarHybridWeightsParameters(
            contentSimilarity=settings.similar.content,
            coPurchaseSimilarity=settings.similar.association,
            popularity=settings.similar.popularity,
        ),
        candidatePoolMultiplier=settings.candidate_pool_multiplier,
        candidatePoolCap=settings.candidate_pool_cap,
        contentAffinityAggregation="maximum_similarity",
        missingComponentPolicy="fallback_to_als_without_renormalization",
    )


def _serialize_neighbors(
    rows: tuple[tuple[ScoredProductIndex, ...], ...],
) -> list[list[dict[str, int | float]]]:
    return [
        [
            {
                "productIndex": item.product_index,
                "score": item.score,
            }
            for item in row
        ]
        for row in rows
    ]
