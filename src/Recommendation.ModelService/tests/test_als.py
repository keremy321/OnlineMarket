from __future__ import annotations

import math

from app.als import aggregate_interactions, interaction_weight
from app.contracts import ModelTrainingRequest


def test_interaction_aggregation_is_deterministic_and_counts_repeats(
    als_training_payload: dict[str, object],
) -> None:
    request = ModelTrainingRequest.model_validate(als_training_payload)

    first = aggregate_interactions(request.interactions)
    second = aggregate_interactions(list(reversed(request.interactions)))

    assert first == second
    subject_a_product_one = first[0]
    assert subject_a_product_one.total_quantity == 3
    assert subject_a_product_one.repeat_purchase_count == 2
    assert subject_a_product_one.weight == interaction_weight(3, 2)
    assert math.isclose(
        subject_a_product_one.weight,
        1 + math.log1p(3) + math.log1p(1),
    )


def test_interaction_weight_rejects_invalid_aggregates() -> None:
    for total_quantity, repeat_count in ((0, 1), (1, 0), (-1, 1)):
        try:
            interaction_weight(total_quantity, repeat_count)
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid interaction aggregate was accepted.")
