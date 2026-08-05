# Recommendation.ModelService

Internal FastAPI service for versioned product-content models. It has no
database connection and accepts only recommendation snapshots supplied by
`Recommendation.Api`.

## Local startup

Use Python 3.13, create a virtual environment, and install the pinned packages:

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements-dev.txt
$env:RECOMMENDATION_MODEL_API_KEY = '<LOCAL_ONLY_SECRET>'
.\.venv\Scripts\python -m uvicorn app.main:app --host 127.0.0.1 --port 8085 --no-access-log
```

Run these commands from `src/Recommendation.ModelService`. Check startup at
`GET http://127.0.0.1:8085/health`. Model endpoints require the `X-Api-Key`
header. The health endpoint intentionally does not require authentication so
container health checks do not need a secret.

Artifacts are written atomically under `artifacts/`. The newest valid joblib
artifact is loaded at startup; corrupt artifacts are ignored. Generated
artifacts are not source controlled.

## Verification

```powershell
python -m pytest
python -m ruff check .
python -m mypy
```

The service logs model version, correlation ID, and record counts only. API
keys and training/inference payloads must never be logged.
