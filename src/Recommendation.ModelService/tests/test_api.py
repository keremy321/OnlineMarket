from __future__ import annotations

from copy import deepcopy
from typing import Any

from conftest import SOURCE_ID
from fastapi.testclient import TestClient
from httpx import Response


def test_health_endpoint_is_public(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "healthy"}


def test_model_endpoints_require_authentication(
    client: TestClient,
    training_payload: dict[str, object],
) -> None:
    missing = client.post("/api/v1/models/train", json=training_payload)
    invalid = client.post(
        "/api/v1/models/train",
        json=training_payload,
        headers={"X-Api-Key": "wrong"},
    )

    assert missing.status_code == 401
    assert invalid.status_code == 401
    assert invalid.json()["code"] == "Authentication.ApiKeyInvalid"


def test_training_contract_rejects_extra_and_empty_data(
    client: TestClient,
    auth_headers: dict[str, str],
    training_payload: dict[str, object],
) -> None:
    with_extra = deepcopy(training_payload)
    with_extra["customerId"] = "50000000-0000-0000-0000-000000000001"
    empty = deepcopy(training_payload)
    empty["products"] = []

    extra_response = client.post(
        "/api/v1/models/train",
        json=with_extra,
        headers=auth_headers,
    )
    empty_response = client.post(
        "/api/v1/models/train",
        json=empty,
        headers=auth_headers,
    )

    assert extra_response.status_code == 422
    assert empty_response.status_code == 422


def test_training_contract_rejects_duplicate_and_unknown_product_ids(
    client: TestClient,
    auth_headers: dict[str, str],
    training_payload: dict[str, object],
) -> None:
    duplicate = deepcopy(training_payload)
    products = duplicate["products"]
    assert isinstance(products, list)
    products.append(deepcopy(products[0]))
    unknown_interaction = deepcopy(training_payload)
    interactions = unknown_interaction["interactions"]
    assert isinstance(interactions, list)
    first_interaction = interactions[0]
    assert isinstance(first_interaction, dict)
    items = first_interaction["items"]
    assert isinstance(items, list)
    items.append(
        {
            "productId": "90000000-0000-0000-0000-000000000001",
            "quantity": 1,
        }
    )

    duplicate_response = client.post(
        "/api/v1/models/train",
        json=duplicate,
        headers=auth_headers,
    )
    unknown_response = client.post(
        "/api/v1/models/train",
        json=unknown_interaction,
        headers=auth_headers,
    )

    assert duplicate_response.status_code == 422
    assert unknown_response.status_code == 422


def test_training_and_similarity_filter_and_order_deterministically(
    client: TestClient,
    auth_headers: dict[str, str],
    training_payload: dict[str, object],
) -> None:
    training = client.post(
        "/api/v1/models/train",
        json=training_payload,
        headers=auth_headers,
    )
    first = infer(client, auth_headers, limit=100)
    second = infer(client, auth_headers, limit=100)

    assert training.status_code == 200
    metadata = training.json()["metadata"]
    assert metadata["modelVersion"] == "tfidf-test-v1"
    assert metadata["productCount"] == 5
    assert metadata["algorithm"] == "tfidf-product-content-cosine-v1"
    assert len(metadata["inputHash"]) == 64
    assert "scikit-learn" in metadata["libraryVersions"]
    assert first.status_code == 200
    assert first.json() == second.json()

    items: list[dict[str, Any]] = first.json()["items"]
    ids = [item["productId"] for item in items]
    assert str(SOURCE_ID) not in ids
    assert "00000000-0000-0000-0000-000000000004" not in ids
    assert "00000000-0000-0000-0000-000000000005" not in ids
    assert ids[0] == "00000000-0000-0000-0000-000000000002"
    assert all(0 <= item["tfidfScore"] <= 1 for item in items)
    assert items == sorted(
        items,
        key=lambda item: (-item["tfidfScore"], item["productId"]),
    )


def test_similarity_limit_is_validated_and_capped_by_contract(
    client: TestClient,
    auth_headers: dict[str, str],
) -> None:
    zero = infer(client, auth_headers, limit=0)
    excessive = infer(client, auth_headers, limit=101)

    assert zero.status_code == 422
    assert excessive.status_code == 422


def test_inference_and_current_report_unavailable_without_model(
    client: TestClient,
    auth_headers: dict[str, str],
) -> None:
    inference = infer(client, auth_headers, limit=10)
    current = client.get("/api/v1/models/current", headers=auth_headers)

    assert inference.status_code == 503
    assert current.status_code == 503
    assert inference.json() == {
        "code": "RecommendationModel.Unavailable",
        "message": "No valid recommendation model is available.",
        "retryable": True,
    }


def infer(
    client: TestClient,
    headers: dict[str, str],
    *,
    limit: int,
) -> Response:
    return client.post(
        "/api/v1/models/similar",
        json={"productId": str(SOURCE_ID), "limit": limit},
        headers=headers,
    )
