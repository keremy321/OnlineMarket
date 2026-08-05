from __future__ import annotations

import pytest

from app.config import AlsSettings


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
