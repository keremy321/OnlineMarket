# Recommendation.ModelService

Internal FastAPI service for versioned TF-IDF and implicit-ALS models. It has no
database connection and accepts only recommendation snapshots supplied by
`Recommendation.Api`.

Order interactions carry a versioned opaque `subjectId`. Direct market
`CustomerId`, the HMAC derivation key, and customer-to-subject mappings are
forbidden in Python requests, artifacts, and logs. SubjectId is pseudonymous,
not fully anonymous.

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

Artifact schema v2 adds an optional ALS model, deterministic SubjectId/product
mappings, and purchased-product filtering history. Schema-v1 TF-IDF-only
artifacts remain loadable and continue serving Similar Products. The ALS
implementation uses pinned `implicit` 0.7.3 on Python 3.13.7; the slim container
installs only the required `libgomp1` OpenMP runtime.

The v2 SubjectId mapping and purchase-history sets are pseudonymous, linkable
model data. The artifact volume therefore requires restricted access. Direct
CustomerId, the derivation key, customer-to-subject mappings, connection
strings, and API keys remain forbidden in artifacts and reports.

ALS settings use `RECOMMENDATION_ALS_FACTORS`,
`RECOMMENDATION_ALS_REGULARIZATION`, `RECOMMENDATION_ALS_ITERATIONS`,
`RECOMMENDATION_ALS_ALPHA`, `RECOMMENDATION_ALS_RANDOM_SEED`,
`RECOMMENDATION_ALS_DEFAULT_LIMIT`, and
`RECOMMENDATION_ALS_MAXIMUM_LIMIT`. The minimum-subject, product, and
aggregated-interaction thresholds are configurable with the corresponding
`RECOMMENDATION_ALS_MINIMUM_*` variables. Invalid values fail startup.

`POST /api/v1/models/evaluate` performs deterministic per-subject temporal
evaluation without reading or changing the active artifact. It evaluates
Popularity and implicit ALS, reports TF-IDF and frequently-bought-together as
`NotEvaluated`, and atomically writes aggregate JSON and Markdown reports under
`evaluation-reports/`. Configure it with `RECOMMENDATION_EVALUATION_K`,
`RECOMMENDATION_EVALUATION_MINIMUM_HISTORICAL_ORDERS`,
`RECOMMENDATION_EVALUATION_HOLDOUT_ORDER_COUNT`,
`RECOMMENDATION_EVALUATION_EXCLUDE_PREVIOUSLY_PURCHASED`,
`RECOMMENDATION_EVALUATION_RANDOM_SEED`,
`RECOMMENDATION_EVALUATION_OUTPUT_DIRECTORY`, and
`RECOMMENDATION_EVALUATION_MAXIMUM_SUBJECTS` (`0` means no cap).

## Verification

```powershell
python -m pytest
python -m ruff check .
python -m mypy
```

The service logs model version, correlation ID, and record counts only. API
keys and training/inference payloads must never be logged.
