from __future__ import annotations

import pytest

from app.config import AlsSettings, EvaluationSettings, Settings


@pytest.mark.parametrize(
    "settings",
    [
        AlsSettings(factors=0),
        AlsSettings(regularization=0),
        AlsSettings(iterations=0),
        AlsSettings(alpha=0),
        AlsSettings(random_seed=-1),
        AlsSettings(default_limit=0),
        AlsSettings(default_limit=51, maximum_limit=50),
        AlsSettings(maximum_limit=1_001),
    ],
)
def test_invalid_als_settings_are_rejected(settings: AlsSettings) -> None:
    with pytest.raises(ValueError):
        settings.validate()


@pytest.mark.parametrize(
    "settings",
    [
        EvaluationSettings(k=0),
        EvaluationSettings(k=101),
        EvaluationSettings(minimum_historical_orders_per_subject=1),
        EvaluationSettings(holdout_order_count=0),
        EvaluationSettings(
            minimum_historical_orders_per_subject=2,
            holdout_order_count=2,
        ),
        EvaluationSettings(random_seed=-1),
        EvaluationSettings(maximum_subjects=0),
    ],
)
def test_invalid_evaluation_settings_are_rejected(
    settings: EvaluationSettings,
) -> None:
    with pytest.raises(ValueError):
        settings.validate()


def test_evaluation_and_artifact_directories_must_be_separate() -> None:
    settings = Settings(
        "key",
        EvaluationSettings().output_directory,
        evaluation=EvaluationSettings(),
    )

    with pytest.raises(ValueError):
        settings.validate()
