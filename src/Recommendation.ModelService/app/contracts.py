from __future__ import annotations

from datetime import datetime
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


class ArtifactMetadata(StrictContract):
    modelVersion: str
    correlationId: UUID
    trainedAtUtc: datetime
    productCount: int
    inputHash: str
    algorithm: str
    libraryVersions: dict[str, str]


class ModelTrainingResponse(StrictContract):
    status: str
    metadata: ArtifactMetadata


class SimilarProductItem(StrictContract):
    productId: UUID
    tfidfScore: float = Field(ge=0, le=1)


class SimilarProductsResponse(StrictContract):
    modelVersion: str
    sourceProductId: UUID
    items: list[SimilarProductItem]


class ApiErrorResponse(StrictContract):
    code: str
    message: str
    retryable: bool
