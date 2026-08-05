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
    evaluation_payload: dict[str, object],
) -> None:
    missing = client.post("/api/v1/models/train", json=training_payload)
    invalid = client.post(
        "/api/v1/models/train",
        json=training_payload,
        headers={"X-Api-Key": "wrong"},
    )
    personalized = client.post(
        "/api/v1/models/personalized",
        json={
            "subjectId": "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "limit": 8,
            "excludePreviouslyPurchased": True,
        },
    )
    evaluation = client.post(
        "/api/v1/models/evaluate",
        json=evaluation_payload,
    )

    assert missing.status_code == 401
    assert invalid.status_code == 401
    assert personalized.status_code == 401
    assert evaluation.status_code == 401
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


def test_empty_interactions_preserve_tfidf_with_clear_als_status(
    client: TestClient,
    auth_headers: dict[str, str],
    training_payload: dict[str, object],
) -> None:
    payload = deepcopy(training_payload)
    payload["interactions"] = []

    response = client.post(
        "/api/v1/models/train",
        json=payload,
        headers=auth_headers,
    )

    assert response.status_code == 200
    metadata = response.json()["metadata"]
    assert metadata["subjectCount"] == 0
    assert metadata["interactionCount"] == 0
    assert metadata["components"]["tfidf"]["status"] == "Succeeded"
    assert metadata["components"]["als"]["status"] == "InsufficientData"


def test_training_contract_requires_opaque_subject_id_and_rejects_customer_id(
    client: TestClient,
    auth_headers: dict[str, str],
    training_payload: dict[str, object],
) -> None:
    missing = deepcopy(training_payload)
    empty = deepcopy(training_payload)
    invalid = deepcopy(training_payload)
    with_customer_id = deepcopy(training_payload)

    for payload, subject_id in (
        (missing, None),
        (empty, ""),
        (invalid, "v1.not-a-valid-hmac"),
    ):
        interactions = payload["interactions"]
        assert isinstance(interactions, list)
        interaction = interactions[0]
        assert isinstance(interaction, dict)
        if subject_id is None:
            interaction.pop("subjectId")
        else:
            interaction["subjectId"] = subject_id

    customer_interactions = with_customer_id["interactions"]
    assert isinstance(customer_interactions, list)
    customer_interaction = customer_interactions[0]
    assert isinstance(customer_interaction, dict)
    customer_interaction["customerId"] = "50000000-0000-0000-0000-000000000001"

    for payload in (missing, empty, invalid, with_customer_id):
        response = client.post(
            "/api/v1/models/train",
            json=payload,
            headers=auth_headers,
        )
        assert response.status_code == 422


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
    assert metadata["subjectCount"] == 1
    assert metadata["interactionCount"] == 2
    assert metadata["algorithm"] == (
        "tfidf-product-content-cosine-v1+quantity-popularity-v1+"
        "directional-copurchase-v1+deterministic-hybrid-ranker-v1"
    )
    assert metadata["algorithmComponents"] == [
        "tfidf-product-content-cosine-v1",
        "quantity-popularity-v1",
        "directional-copurchase-v1",
        "deterministic-hybrid-ranker-v1",
    ]
    assert metadata["components"]["tfidf"]["status"] == "Succeeded"
    assert metadata["components"]["als"]["status"] == "InsufficientData"
    assert metadata["components"]["hybrid"]["status"] == "Succeeded"
    assert metadata["hybridParameters"]["personalizedWeights"] == {
        "als": 0.5,
        "contentAffinity": 0.2,
        "association": 0.15,
        "popularity": 0.15,
    }
    assert metadata["alsParameters"]["randomSeed"] == 42
    assert len(metadata["inputHash"]) == 64
    assert "scikit-learn" in metadata["libraryVersions"]
    assert "implicit" in metadata["libraryVersions"]
    assert first.status_code == 200
    assert first.json() == second.json()
    assert first.json()["strategy"] == "HybridSimilar"

    items: list[dict[str, Any]] = first.json()["items"]
    ids = [item["productId"] for item in items]
    assert str(SOURCE_ID) not in ids
    assert "00000000-0000-0000-0000-000000000004" not in ids
    assert "00000000-0000-0000-0000-000000000005" not in ids
    assert ids[0] == "00000000-0000-0000-0000-000000000002"
    assert all(0 <= item["tfidfScore"] <= 1 for item in items)
    assert all(0 <= item["finalScore"] <= 1 for item in items)
    assert items == sorted(
        items,
        key=lambda item: (-item["finalScore"], item["productId"]),
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


def test_personalized_known_subject_is_deterministic_and_filtered(
    client: TestClient,
    auth_headers: dict[str, str],
    als_training_payload: dict[str, object],
) -> None:
    training = client.post(
        "/api/v1/models/train",
        json=als_training_payload,
        headers=auth_headers,
    )
    first = infer_personalized(client, auth_headers, limit=100)
    second = infer_personalized(client, auth_headers, limit=100)

    assert training.status_code == 200
    metadata = training.json()["metadata"]
    assert metadata["subjectCount"] == 3
    assert metadata["interactionCount"] == 7
    assert metadata["components"]["als"]["status"] == "Succeeded"
    assert metadata["algorithmComponents"] == [
        "tfidf-product-content-cosine-v1",
        "implicit-als-v1",
        "quantity-popularity-v1",
        "directional-copurchase-v1",
        "deterministic-hybrid-ranker-v1",
    ]
    assert first.status_code == 200
    assert first.json() == second.json()
    body = first.json()
    assert body["strategy"] == "implicit_als"
    recommendations: list[dict[str, Any]] = body["recommendations"]
    assert [item["productId"] for item in recommendations] == [
        "00000000-0000-0000-0000-000000000003"
    ]
    assert all(0 <= item["score"] <= 1 for item in recommendations)


def test_personalized_hybrid_returns_component_diagnostics(
    client: TestClient,
    auth_headers: dict[str, str],
    als_training_payload: dict[str, object],
) -> None:
    client.post(
        "/api/v1/models/train",
        json=als_training_payload,
        headers=auth_headers,
    )

    first = infer_personalized(client, auth_headers, limit=10, strategy="Hybrid")
    second = infer_personalized(client, auth_headers, limit=10, strategy="Hybrid")

    assert first.status_code == 200
    assert first.json() == second.json()
    body = first.json()
    assert body["strategy"] == "HybridPersonalized"
    assert body["recommendations"]
    for item in body["recommendations"]:
        assert item["score"] == item["finalScore"]
        assert all(
            0 <= item[field] <= 1
            for field in (
                "alsScore",
                "contentAffinityScore",
                "associationScore",
                "popularityScore",
                "finalScore",
            )
        )


def test_personalized_unknown_subject_and_limit_contract(
    client: TestClient,
    auth_headers: dict[str, str],
    als_training_payload: dict[str, object],
) -> None:
    client.post(
        "/api/v1/models/train",
        json=als_training_payload,
        headers=auth_headers,
    )
    unknown = infer_personalized(
        client,
        auth_headers,
        subject_id="v1.ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ",
        limit=8,
        strategy="Hybrid",
    )
    invalid = infer_personalized(client, auth_headers, limit=0)
    capped = infer_personalized(client, auth_headers, limit=100)

    assert unknown.status_code == 200
    assert unknown.json()["strategy"] == "cold_start_unavailable"
    assert unknown.json()["recommendations"] == []
    assert invalid.status_code == 422
    assert capped.status_code == 200
    assert len(capped.json()["recommendations"]) <= 50


def test_personalized_contract_rejects_invalid_subject_and_unknown_fields(
    client: TestClient,
    auth_headers: dict[str, str],
) -> None:
    invalid = client.post(
        "/api/v1/models/personalized",
        json={"subjectId": "not-valid", "limit": 8},
        headers=auth_headers,
    )
    unknown = client.post(
        "/api/v1/models/personalized",
        json={
            "subjectId": "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "limit": 8,
            "customerId": "50000000-0000-0000-0000-000000000001",
        },
        headers=auth_headers,
    )

    assert invalid.status_code == 422
    assert unknown.status_code == 422


def test_evaluation_endpoint_returns_metrics_without_identifiers(
    client: TestClient,
    auth_headers: dict[str, str],
    evaluation_payload: dict[str, object],
) -> None:
    response = client.post(
        "/api/v1/models/evaluate",
        json=evaluation_payload,
        headers=auth_headers,
    )

    assert response.status_code == 200
    body = response.json()
    serialized = response.text
    assert body["status"] == "Succeeded"
    assert body["models"]["popularity"]["status"] == "Evaluated"
    assert body["models"]["als"]["status"] == "Evaluated"
    assert body["models"]["hybrid"]["status"] == "Evaluated"
    assert body["comparison"] is not None
    assert body["models"]["tfidf"]["status"] == "NotEvaluated"
    assert body["models"]["fbt"]["status"] == "NotEvaluated"
    assert "subjectId" not in serialized
    assert "customerId" not in serialized
    assert "v1.AAA" not in serialized


def test_evaluation_contract_is_strict_and_requires_utc_timestamps(
    client: TestClient,
    auth_headers: dict[str, str],
    evaluation_payload: dict[str, object],
) -> None:
    with_extra = deepcopy(evaluation_payload)
    with_extra["customerId"] = "50000000-0000-0000-0000-000000000001"
    without_utc = deepcopy(evaluation_payload)
    interactions = without_utc["interactions"]
    assert isinstance(interactions, list)
    first = interactions[0]
    assert isinstance(first, dict)
    first["occurredAtUtc"] = "2026-01-01T00:00:00"

    extra = client.post(
        "/api/v1/models/evaluate",
        json=with_extra,
        headers=auth_headers,
    )
    invalid_time = client.post(
        "/api/v1/models/evaluate",
        json=without_utc,
        headers=auth_headers,
    )

    assert extra.status_code == 422
    assert invalid_time.status_code == 422


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


def infer_personalized(
    client: TestClient,
    headers: dict[str, str],
    *,
    subject_id: str = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
    limit: int,
    strategy: str | None = None,
) -> Response:
    request: dict[str, object] = {
        "subjectId": subject_id,
        "limit": limit,
        "excludePreviouslyPurchased": True,
    }
    if strategy is not None:
        request["strategy"] = strategy
    return client.post(
        "/api/v1/models/personalized",
        json=request,
        headers=headers,
    )
