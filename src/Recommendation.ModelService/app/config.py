from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path


@dataclass(frozen=True, slots=True)
class AlsSettings:
    factors: int = 32
    regularization: float = 0.05
    iterations: int = 20
    alpha: float = 20.0
    random_seed: int = 42
    default_limit: int = 8
    maximum_limit: int = 50
    minimum_subjects: int = 2
    minimum_products: int = 2
    minimum_interactions: int = 3

    @classmethod
    def from_environment(cls) -> AlsSettings:
        return cls(
            factors=_integer("RECOMMENDATION_ALS_FACTORS", 32),
            regularization=_number(
                "RECOMMENDATION_ALS_REGULARIZATION",
                0.05,
            ),
            iterations=_integer("RECOMMENDATION_ALS_ITERATIONS", 20),
            alpha=_number("RECOMMENDATION_ALS_ALPHA", 20.0),
            random_seed=_integer("RECOMMENDATION_ALS_RANDOM_SEED", 42),
            default_limit=_integer(
                "RECOMMENDATION_ALS_DEFAULT_LIMIT",
                8,
            ),
            maximum_limit=_integer(
                "RECOMMENDATION_ALS_MAXIMUM_LIMIT",
                50,
            ),
            minimum_subjects=_integer(
                "RECOMMENDATION_ALS_MINIMUM_SUBJECTS",
                2,
            ),
            minimum_products=_integer(
                "RECOMMENDATION_ALS_MINIMUM_PRODUCTS",
                2,
            ),
            minimum_interactions=_integer(
                "RECOMMENDATION_ALS_MINIMUM_INTERACTIONS",
                3,
            ),
        )

    def validate(self) -> None:
        if not 1 <= self.factors <= 512:
            raise ValueError("ALS factors must be between 1 and 512.")
        if not 0 < self.regularization <= 10:
            raise ValueError("ALS regularization must be in (0, 10].")
        if not 1 <= self.iterations <= 1_000:
            raise ValueError("ALS iterations must be between 1 and 1000.")
        if not 0 < self.alpha <= 10_000:
            raise ValueError("ALS alpha must be in (0, 10000].")
        if not 0 <= self.random_seed <= 2_147_483_647:
            raise ValueError("ALS random seed is outside the supported range.")
        if not 1 <= self.default_limit <= self.maximum_limit:
            raise ValueError(
                "ALS default limit must be positive and no greater than the maximum."
            )
        if not self.maximum_limit <= 1_000:
            raise ValueError("ALS maximum limit must not exceed 1000.")
        if self.minimum_subjects < 2:
            raise ValueError("ALS minimum subjects must be at least 2.")
        if self.minimum_products < 2:
            raise ValueError("ALS minimum products must be at least 2.")
        if self.minimum_interactions < 2:
            raise ValueError("ALS minimum interactions must be at least 2.")


@dataclass(frozen=True, slots=True)
class Settings:
    api_key: str
    artifact_directory: Path
    als: AlsSettings = field(default_factory=AlsSettings)

    @classmethod
    def from_environment(cls) -> Settings:
        return cls(
            api_key=os.getenv("RECOMMENDATION_MODEL_API_KEY", ""),
            artifact_directory=Path(
                os.getenv(
                    "RECOMMENDATION_MODEL_ARTIFACT_DIRECTORY",
                    "artifacts",
                )
            ),
            als=AlsSettings.from_environment(),
        )

    def validate(self) -> None:
        self.als.validate()


def _integer(name: str, default: int) -> int:
    value = os.getenv(name)
    if value is None:
        return default
    try:
        return int(value)
    except ValueError as exception:
        raise ValueError(f"{name} must be an integer.") from exception


def _number(name: str, default: float) -> float:
    value = os.getenv(name)
    if value is None:
        return default
    try:
        return float(value)
    except ValueError as exception:
        raise ValueError(f"{name} must be a number.") from exception
