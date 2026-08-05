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
class EvaluationSettings:
    k: int = 5
    minimum_historical_orders_per_subject: int = 2
    holdout_order_count: int = 1
    exclude_previously_purchased: bool = True
    random_seed: int = 42
    output_directory: Path = Path("evaluation-reports")
    maximum_subjects: int | None = None

    @classmethod
    def from_environment(cls) -> EvaluationSettings:
        return cls(
            k=_integer("RECOMMENDATION_EVALUATION_K", 5),
            minimum_historical_orders_per_subject=_integer(
                "RECOMMENDATION_EVALUATION_MINIMUM_HISTORICAL_ORDERS",
                2,
            ),
            holdout_order_count=_integer(
                "RECOMMENDATION_EVALUATION_HOLDOUT_ORDER_COUNT",
                1,
            ),
            exclude_previously_purchased=_boolean(
                "RECOMMENDATION_EVALUATION_EXCLUDE_PREVIOUSLY_PURCHASED",
                True,
            ),
            random_seed=_integer(
                "RECOMMENDATION_EVALUATION_RANDOM_SEED",
                42,
            ),
            output_directory=Path(
                os.getenv(
                    "RECOMMENDATION_EVALUATION_OUTPUT_DIRECTORY",
                    "evaluation-reports",
                )
            ),
            maximum_subjects=_optional_positive_integer(
                "RECOMMENDATION_EVALUATION_MAXIMUM_SUBJECTS"
            ),
        )

    def validate(self) -> None:
        if not 1 <= self.k <= 100:
            raise ValueError("Evaluation K must be between 1 and 100.")
        if not 2 <= self.minimum_historical_orders_per_subject <= 10_000:
            raise ValueError(
                "Evaluation minimum historical orders must be between 2 and 10000."
            )
        if not 1 <= self.holdout_order_count < (
            self.minimum_historical_orders_per_subject
        ):
            raise ValueError(
                "Evaluation holdout orders must be positive and leave at least one "
                "historical training order."
            )
        if not 0 <= self.random_seed <= 2_147_483_647:
            raise ValueError("Evaluation random seed is outside the supported range.")
        if not str(self.output_directory).strip():
            raise ValueError("Evaluation output directory is required.")
        if self.maximum_subjects is not None and self.maximum_subjects <= 0:
            raise ValueError("Evaluation maximum subjects must be positive.")


@dataclass(frozen=True, slots=True)
class Settings:
    api_key: str
    artifact_directory: Path
    als: AlsSettings = field(default_factory=AlsSettings)
    evaluation: EvaluationSettings = field(default_factory=EvaluationSettings)

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
            evaluation=EvaluationSettings.from_environment(),
        )

    def validate(self) -> None:
        self.als.validate()
        self.evaluation.validate()
        if (
            self.artifact_directory.resolve()
            == self.evaluation.output_directory.resolve()
        ):
            raise ValueError(
                "Evaluation reports and model artifacts require separate directories."
            )


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


def _boolean(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    normalized = value.strip().lower()
    if normalized in {"1", "true", "yes"}:
        return True
    if normalized in {"0", "false", "no"}:
        return False
    raise ValueError(f"{name} must be a boolean.")


def _optional_positive_integer(name: str) -> int | None:
    value = os.getenv(name)
    if value is None or value.strip() in {"", "0"}:
        return None
    try:
        parsed = int(value)
    except ValueError as exception:
        raise ValueError(f"{name} must be an integer.") from exception
    if parsed <= 0:
        raise ValueError(f"{name} must be positive or zero for no cap.")
    return parsed
