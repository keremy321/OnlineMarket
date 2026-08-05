from __future__ import annotations

from datetime import datetime, timedelta
from decimal import Decimal
from enum import StrEnum
from typing import Annotated
from uuid import UUID

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    StringConstraints,
    field_validator,
    model_validator,
)

NonBlankText = Annotated[
    str,
    StringConstraints(strip_whitespace=True, min_length=1),
]

SUBJECT_ID_PATTERN = r"^v[1-9][0-9]{0,14}\.[A-Za-z0-9_-]{43}$"

SubjectId = Annotated[
    str,
    StringConstraints(
        strip_whitespace=True,
        min_length=46,
        max_length=64,
        pattern=SUBJECT_ID_PATTERN,
    ),
]


class StrictContract(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        populate_by_name=False,
        str_strip_whitespace=True,
    )


class UnitType(StrEnum):
    PIECE = "Piece"
    GRAM = "Gram"
    KILOGRAM = "Kilogram"
    MILLILITRE = "Millilitre"
    LITRE = "Litre"
    PACKAGE = "Package"


class ProductTrainingSnapshot(StrictContract):
    productId: UUID
    categoryId: UUID
    brandId: UUID
    name: Annotated[NonBlankText, StringConstraints(max_length=200)]
    description: Annotated[str, StringConstraints(max_length=2000)] | None = None
    unitType: UnitType
    netContent: Decimal = Field(gt=0, max_digits=12, decimal_places=3)
    price: Decimal = Field(ge=0, max_digits=18, decimal_places=2)
    isActive: bool
    isInStock: bool

    @field_validator("productId", "categoryId", "brandId")
    @classmethod
    def ids_must_not_be_empty(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("Identifiers must not be empty UUIDs.")
        return value


class OrderInteractionItem(StrictContract):
    productId: UUID
    quantity: int = Field(gt=0, le=2_147_483_647)

    @field_validator("productId")
    @classmethod
    def product_id_must_not_be_empty(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("productId must not be an empty UUID.")
        return value


class OrderProductInteraction(StrictContract):
    orderId: UUID
    subjectId: SubjectId
    items: list[OrderInteractionItem] = Field(min_length=1, max_length=1000)

    @field_validator("orderId")
    @classmethod
    def order_id_must_not_be_empty(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("orderId must not be an empty UUID.")
        return value

    @model_validator(mode="after")
    def product_ids_must_be_unique(self) -> OrderProductInteraction:
        product_ids = [item.productId for item in self.items]
        if len(product_ids) != len(set(product_ids)):
            raise ValueError("Interaction productId values must be unique.")
        return self


class ModelTrainingRequest(StrictContract):
    modelVersion: Annotated[
        NonBlankText,
        StringConstraints(max_length=100, pattern=r"^[A-Za-z0-9._-]+$"),
    ]
    correlationId: UUID
    products: list[ProductTrainingSnapshot] = Field(
        min_length=1,
        max_length=50_000,
    )
    interactions: list[OrderProductInteraction] = Field(
        default_factory=list,
        max_length=100_000,
    )

    @field_validator("correlationId")
    @classmethod
    def correlation_id_must_not_be_empty(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("correlationId must not be an empty UUID.")
        return value

    @model_validator(mode="after")
    def training_ids_must_be_unique(self) -> ModelTrainingRequest:
        product_ids = [product.productId for product in self.products]
        order_ids = [interaction.orderId for interaction in self.interactions]
        if len(product_ids) != len(set(product_ids)):
            raise ValueError("Training productId values must be unique.")
        if len(order_ids) != len(set(order_ids)):
            raise ValueError("Training orderId values must be unique.")
        known_products = set(product_ids)
        if any(
            item.productId not in known_products
            for interaction in self.interactions
            for item in interaction.items
        ):
            raise ValueError(
                "Interaction productId values must reference training products."
            )
        return self


class SimilarProductsRequest(StrictContract):
    productId: UUID
    limit: int = Field(default=10, ge=1, le=100)

    @field_validator("productId")
    @classmethod
    def product_id_must_not_be_empty(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("productId must not be an empty UUID.")
        return value


class ModelComponentState(StrEnum):
    SUCCEEDED = "Succeeded"
    INSUFFICIENT_DATA = "InsufficientData"


class ModelComponentStatus(StrictContract):
    status: ModelComponentState
    trainingDurationMilliseconds: int = Field(ge=0)


class ModelComponentStatuses(StrictContract):
    tfidf: ModelComponentStatus
    als: ModelComponentStatus
    popularity: ModelComponentStatus | None = None
    association: ModelComponentStatus | None = None
    hybrid: ModelComponentStatus | None = None


class AlsParameters(StrictContract):
    factors: int = Field(gt=0)
    regularization: float = Field(gt=0)
    iterations: int = Field(gt=0)
    alpha: float = Field(gt=0)
    randomSeed: int = Field(ge=0)


class PersonalizedHybridWeightsParameters(StrictContract):
    als: float = Field(ge=0, le=1)
    contentAffinity: float = Field(ge=0, le=1)
    association: float = Field(ge=0, le=1)
    popularity: float = Field(ge=0, le=1)


class SimilarHybridWeightsParameters(StrictContract):
    contentSimilarity: float = Field(ge=0, le=1)
    coPurchaseSimilarity: float = Field(ge=0, le=1)
    popularity: float = Field(ge=0, le=1)


class HybridParameters(StrictContract):
    personalizedWeights: PersonalizedHybridWeightsParameters
    similarWeights: SimilarHybridWeightsParameters
    candidatePoolMultiplier: int = Field(gt=0)
    candidatePoolCap: int = Field(gt=0)
    contentAffinityAggregation: str
    missingComponentPolicy: str


class ArtifactMetadata(StrictContract):
    modelVersion: str
    correlationId: UUID
    trainedAtUtc: datetime
    productCount: int
    subjectCount: int = Field(default=0, ge=0)
    interactionCount: int = Field(default=0, ge=0)
    inputHash: str
    algorithm: str
    algorithmComponents: list[str] = Field(default_factory=list)
    components: ModelComponentStatuses | None = None
    alsParameters: AlsParameters | None = None
    hybridParameters: HybridParameters | None = None
    libraryVersions: dict[str, str]


class ModelTrainingResponse(StrictContract):
    status: str
    metadata: ArtifactMetadata


class SimilarProductItem(StrictContract):
    productId: UUID
    tfidfScore: float = Field(ge=0, le=1)
    coPurchaseScore: float = Field(default=0, ge=0, le=1)
    popularityScore: float = Field(default=0, ge=0, le=1)
    finalScore: float = Field(ge=0, le=1)
    reasonCode: str
    reasonText: str


class SimilarProductsResponse(StrictContract):
    modelVersion: str
    strategy: str
    sourceProductId: UUID
    items: list[SimilarProductItem]


class PersonalizedModelStrategy(StrEnum):
    ALS = "Als"
    HYBRID = "Hybrid"


class PersonalizedRecommendationsRequest(StrictContract):
    subjectId: SubjectId
    limit: int | None = Field(default=None, ge=1, le=1_000)
    excludePreviouslyPurchased: bool = True
    strategy: PersonalizedModelStrategy = PersonalizedModelStrategy.ALS


class PersonalizedRecommendationItem(StrictContract):
    productId: UUID
    score: float = Field(ge=0, le=1)
    confidence: float | None = Field(default=None, ge=0, le=1)
    reasonCode: str
    reasonText: str
    alsScore: float | None = Field(default=None, ge=0, le=1)
    contentAffinityScore: float | None = Field(default=None, ge=0, le=1)
    associationScore: float | None = Field(default=None, ge=0, le=1)
    popularityScore: float | None = Field(default=None, ge=0, le=1)
    finalScore: float | None = Field(default=None, ge=0, le=1)


class PersonalizedRecommendationsResponse(StrictContract):
    modelVersion: str
    strategy: str
    recommendations: list[PersonalizedRecommendationItem]


class EvaluationOrderInteraction(OrderProductInteraction):
    occurredAtUtc: datetime

    @field_validator("occurredAtUtc")
    @classmethod
    def occurred_at_must_be_utc(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() != timedelta(0):
            raise ValueError("occurredAtUtc must be an offset-aware UTC timestamp.")
        return value


class ModelEvaluationRequest(StrictContract):
    evaluationVersion: Annotated[
        NonBlankText,
        StringConstraints(max_length=100, pattern=r"^[A-Za-z0-9._-]+$"),
    ]
    catalogueProductIds: list[UUID] = Field(max_length=50_000)
    candidateProductIds: list[UUID] = Field(max_length=50_000)
    products: list[ProductTrainingSnapshot] = Field(max_length=50_000)
    interactions: list[EvaluationOrderInteraction] = Field(
        default_factory=list,
        max_length=100_000,
    )

    @model_validator(mode="after")
    def evaluation_ids_must_be_valid(self) -> ModelEvaluationRequest:
        if any(product_id.int == 0 for product_id in self.catalogueProductIds):
            raise ValueError("Catalogue product IDs must not be empty UUIDs.")
        if any(product_id.int == 0 for product_id in self.candidateProductIds):
            raise ValueError("Candidate product IDs must not be empty UUIDs.")
        if len(self.catalogueProductIds) != len(set(self.catalogueProductIds)):
            raise ValueError("Catalogue product IDs must be unique.")
        if len(self.candidateProductIds) != len(set(self.candidateProductIds)):
            raise ValueError("Candidate product IDs must be unique.")
        if not set(self.candidateProductIds).issubset(self.catalogueProductIds):
            raise ValueError("Candidate products must belong to the catalogue.")
        product_ids = [product.productId for product in self.products]
        if len(product_ids) != len(set(product_ids)):
            raise ValueError("Evaluation product IDs must be unique.")
        if set(product_ids) != set(self.catalogueProductIds):
            raise ValueError(
                "Evaluation product snapshots must match the catalogue IDs."
            )
        available_product_ids = {
            product.productId
            for product in self.products
            if product.isActive and product.isInStock
        }
        if set(self.candidateProductIds) != available_product_ids:
            raise ValueError(
                "Evaluation candidates must match active, in-stock products."
            )
        order_ids = [interaction.orderId for interaction in self.interactions]
        if len(order_ids) != len(set(order_ids)):
            raise ValueError("Evaluation order IDs must be unique.")
        return self


class EvaluationDatasetCounts(StrictContract):
    productCount: int = Field(ge=0)
    candidateProductCount: int = Field(ge=0)
    subjectCount: int = Field(ge=0)
    orderCount: int = Field(ge=0)
    interactionCount: int = Field(ge=0)


class EvaluationSplitSummary(StrictContract):
    strategy: str
    description: str
    k: int = Field(gt=0)
    minimumHistoricalOrdersPerSubject: int = Field(gt=0)
    holdoutOrderCount: int = Field(gt=0)
    excludePreviouslyPurchased: bool
    randomSeed: int = Field(ge=0)
    eligibleSubjectCount: int = Field(ge=0)
    excludedSubjectCount: int = Field(ge=0)
    trainingOrderCount: int = Field(ge=0)
    testOrderCount: int = Field(ge=0)
    trainingInteractionCount: int = Field(ge=0)
    testInteractionCount: int = Field(ge=0)


class EvaluationExcludedDataCounts(StrictContract):
    insufficientHistorySubjectCount: int = Field(ge=0)
    noUsableTrainingHistorySubjectCount: int = Field(ge=0)
    noUsableTestInteractionsSubjectCount: int = Field(ge=0)
    developmentCapSubjectCount: int = Field(ge=0)
    productsAbsentFromTrainingInteractions: int = Field(ge=0)
    unknownProductInteractionCount: int = Field(ge=0)
    popularityFallbackSubjectCount: int = Field(ge=0)


class EvaluationModelMetrics(StrictContract):
    precisionAtK: float = Field(ge=0, le=1)
    recallAtK: float = Field(ge=0, le=1)
    hitRateAtK: float = Field(ge=0, le=1)
    ndcgAtK: float = Field(ge=0, le=1)
    catalogueCoverage: float = Field(ge=0, le=1)
    knownSubjectCatalogueCoverage: float = Field(ge=0, le=1)
    catalogueCoverageIncludingFallback: float = Field(ge=0, le=1)
    eligibleSubjectCount: int = Field(ge=0)
    trainingInteractionCount: int = Field(ge=0)
    testInteractionCount: int = Field(ge=0)
    trainingDurationMilliseconds: float = Field(ge=0)
    averageInferenceLatencyMilliseconds: float = Field(ge=0)
    p95InferenceLatencyMilliseconds: float = Field(ge=0)
    fallbackSubjectCount: int = Field(ge=0)


class EvaluationModelParameters(StrictContract):
    interactionWeighting: str
    excludePreviouslyPurchased: bool
    factors: int | None = Field(default=None, gt=0)
    regularization: float | None = Field(default=None, gt=0)
    iterations: int | None = Field(default=None, gt=0)
    alpha: float | None = Field(default=None, gt=0)
    randomSeed: int | None = Field(default=None, ge=0)
    hybrid: HybridParameters | None = None


class EvaluationModelResult(StrictContract):
    status: str
    reason: str | None = None
    metrics: EvaluationModelMetrics | None = None
    parameters: EvaluationModelParameters | None = None


class EvaluationModels(StrictContract):
    popularity: EvaluationModelResult
    als: EvaluationModelResult
    hybrid: EvaluationModelResult
    tfidf: EvaluationModelResult
    fbt: EvaluationModelResult


class EvaluationComparison(StrictContract):
    hybridMinusAlsPrecisionAt5: float
    hybridMinusAlsRecallAt5: float
    hybridMinusAlsHitRateAt5: float
    hybridMinusAlsNdcgAt5: float
    hybridMinusAlsCoverage: float


class EvaluationReportFiles(StrictContract):
    jsonFile: str
    markdownFile: str


class ModelEvaluationResponse(StrictContract):
    status: str
    evaluationVersion: str
    evaluatedAtUtc: datetime
    inputHash: str
    dataset: EvaluationDatasetCounts
    split: EvaluationSplitSummary
    excludedData: EvaluationExcludedDataCounts
    models: EvaluationModels
    comparison: EvaluationComparison | None
    reportIdentifier: str
    reports: EvaluationReportFiles
    limitations: list[str]


class ApiErrorResponse(StrictContract):
    code: str
    message: str
    retryable: bool
