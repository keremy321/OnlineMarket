from __future__ import annotations

from collections.abc import Iterator
from copy import deepcopy
from pathlib import Path
from uuid import UUID

import pytest
from fastapi.testclient import TestClient

from app.config import EvaluationSettings, Settings
from app.main import create_app

API_KEY = "test-only-model-service-api-key"
SOURCE_ID = UUID("00000000-0000-0000-0000-000000000001")


@pytest.fixture
def client(tmp_path: Path) -> Iterator[TestClient]:
    app = create_app(
        Settings(
            API_KEY,
            tmp_path / "artifacts",
            evaluation=EvaluationSettings(
                output_directory=tmp_path / "evaluation-reports"
            ),
        )
    )
    with TestClient(app) as test_client:
        yield test_client


@pytest.fixture
def auth_headers() -> dict[str, str]:
    return {"X-Api-Key": API_KEY}


@pytest.fixture
def training_payload() -> dict[str, object]:
    category = "10000000-0000-0000-0000-000000000001"
    brand = "20000000-0000-0000-0000-000000000001"
    return {
        "modelVersion": "tfidf-test-v1",
        "correlationId": "30000000-0000-0000-0000-000000000001",
        "products": [
            product_payload(
                1,
                "Dark roasted filter coffee",
                "Arabica coffee beans",
                category,
                brand,
            ),
            product_payload(
                2,
                "Filter coffee beans",
                "Dark roasted Arabica",
                category,
                brand,
            ),
            product_payload(
                3,
                "Green tea",
                "Loose leaf tea",
                "10000000-0000-0000-0000-000000000002",
                "20000000-0000-0000-0000-000000000002",
            ),
            product_payload(
                4,
                "Coffee capsule",
                "Arabica coffee",
                category,
                brand,
                is_active=False,
            ),
            product_payload(
                5,
                "Ground coffee",
                "Dark roast coffee",
                category,
                brand,
                is_in_stock=False,
            ),
        ],
        "interactions": [
            {
                "orderId": "40000000-0000-0000-0000-000000000001",
                "subjectId": ("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
                "items": [
                    {"productId": str(SOURCE_ID), "quantity": 1},
                    {
                        "productId": "00000000-0000-0000-0000-000000000002",
                        "quantity": 2,
                    },
                ],
            }
        ],
    }


@pytest.fixture
def als_training_payload(
    training_payload: dict[str, object],
) -> dict[str, object]:
    payload = deepcopy(training_payload)
    subject_a = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    subject_b = "v1.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
    subject_c = "v1.CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC"
    payload["modelVersion"] = "model-set-test-v1"
    payload["interactions"] = [
        interaction_payload(1, subject_a, [(1, 1)]),
        interaction_payload(2, subject_a, [(1, 2), (2, 1)]),
        interaction_payload(3, subject_b, [(2, 2), (3, 1)]),
        interaction_payload(4, subject_c, [(1, 1), (3, 2)]),
    ]
    return payload


@pytest.fixture
def evaluation_payload() -> dict[str, object]:
    subject_a = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    subject_b = "v1.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
    subject_c = "v1.CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC"
    return {
        "evaluationVersion": "temporal-test-v1",
        "products": evaluation_products(range(1, 6)),
        "catalogueProductIds": [
            f"00000000-0000-0000-0000-{key:012d}" for key in range(1, 6)
        ],
        "candidateProductIds": [
            f"00000000-0000-0000-0000-{key:012d}" for key in range(1, 6)
        ],
        "interactions": [
            evaluation_interaction(1, subject_a, "2026-01-01T00:00:00Z", [(1, 2)]),
            evaluation_interaction(2, subject_a, "2026-01-02T00:00:00Z", [(2, 1)]),
            evaluation_interaction(3, subject_b, "2026-01-01T00:00:00Z", [(1, 1)]),
            evaluation_interaction(4, subject_b, "2026-01-03T00:00:00Z", [(2, 1)]),
            evaluation_interaction(5, subject_c, "2026-01-01T00:00:00Z", [(3, 1)]),
            evaluation_interaction(6, subject_c, "2026-01-04T00:00:00Z", [(1, 1)]),
        ],
    }


def product_payload(
    key: int,
    name: str,
    description: str,
    category_id: str,
    brand_id: str,
    *,
    is_active: bool = True,
    is_in_stock: bool = True,
) -> dict[str, object]:
    return {
        "productId": f"00000000-0000-0000-0000-{key:012d}",
        "categoryId": category_id,
        "brandId": brand_id,
        "name": name,
        "description": description,
        "unitType": "Gram",
        "netContent": "250.000",
        "price": "100.00",
        "isActive": is_active,
        "isInStock": is_in_stock,
    }


def interaction_payload(
    order_key: int,
    subject_id: str,
    items: list[tuple[int, int]],
) -> dict[str, object]:
    return {
        "orderId": f"40000000-0000-0000-0000-{order_key:012d}",
        "subjectId": subject_id,
        "items": [
            {
                "productId": f"00000000-0000-0000-0000-{product_key:012d}",
                "quantity": quantity,
            }
            for product_key, quantity in items
        ],
    }


def evaluation_interaction(
    order_key: int,
    subject_id: str,
    occurred_at_utc: str,
    items: list[tuple[int, int]],
) -> dict[str, object]:
    payload = interaction_payload(order_key, subject_id, items)
    payload["occurredAtUtc"] = occurred_at_utc
    return payload


def evaluation_products(keys: range | list[int]) -> list[dict[str, object]]:
    return [
        product_payload(
            key,
            f"Evaluation product {key}",
            f"Deterministic evaluation description {key}",
            f"10000000-0000-0000-0000-{((key - 1) % 2) + 1:012d}",
            f"20000000-0000-0000-0000-{((key - 1) % 2) + 1:012d}",
        )
        for key in keys
    ]
