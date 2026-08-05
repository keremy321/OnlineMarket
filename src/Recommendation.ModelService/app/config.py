from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True, slots=True)
class Settings:
    api_key: str
    artifact_directory: Path

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
        )
