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
from .config import AlsSettings
from .contracts import (
    AlsParameters,
    ArtifactMetadata,
    ModelComponentState,
    ModelComponentStatus,
    ModelComponentStatuses,
    ModelTrainingRequest,
    OrderProductInteraction,
    SimilarProductItem,
)
from .features import build_product_document

TFIDF_ALGORITHM = "tfidf-product-content-cosine-v1"
ARTIFACT_SCHEMA_VERSION = 2
LEGACY_ARTIFACT_SCHEMA_VERSION = 1


@dataclass(frozen=True, slots=True)
class TrainedModel:
    metadata: ArtifactMetadata
    vectorizer: TfidfVectorizer
    matrix: csr_matrix
    product_ids: tuple[UUID, ...]
    availability: tuple[bool, ...]
    als: TrainedAlsComponent | None = None

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
    trained_at_utc: datetime | None = None,
) -> TrainedModel:
    resolved_als_settings = als_settings or AlsSettings()
    resolved_als_settings.validate()
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
    subject_count = len({interaction.subjectId for interaction in request.interactions})
    interaction_count = sum(
        len(interaction.items) for interaction in request.interactions
    )
    trained_at = trained_at_utc or datetime.now(UTC)
    algorithm_components = [TFIDF_ALGORITHM]
    if als is not None:
        algorithm_components.append(ALS_ALGORITHM)
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
        ),
        alsParameters=AlsParameters(
            factors=resolved_als_settings.factors,
            regularization=resolved_als_settings.regularization,
            iterations=resolved_als_settings.iterations,
            alpha=resolved_als_settings.alpha,
            randomSeed=resolved_als_settings.random_seed,
        ),
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
    )


def _duration_milliseconds(started: float) -> int:
    return max(0, round((perf_counter() - started) * 1_000))


def find_similar(
    model: TrainedModel,
    source_product_id: UUID,
    limit: int,
) -> list[SimilarProductItem]:
    try:
        source_index = model.product_ids.index(source_product_id)
    except ValueError:
        return []

    similarities = cosine_similarity(
        model.matrix[source_index],
        model.matrix,
    ).ravel()
    candidates = [
        SimilarProductItem(
            productId=product_id,
            tfidfScore=round(float(np.clip(similarities[index], 0, 1)), 12),
        )
        for index, product_id in enumerate(model.product_ids)
        if index != source_index and model.availability[index]
    ]
    candidates.sort(key=lambda item: (-item.tfidfScore, item.productId.hex))
    return candidates[:limit]
