# Recommendation.ModelService

Internal FastAPI service for versioned TF-IDF, implicit-ALS, and deterministic
hybrid recommendation models. It has no database connection and accepts only
recommendation snapshots supplied by `Recommendation.Api`.

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

Artifact schema v3 adds normalized popularity, sparse directional co-purchase
and TF-IDF neighbors, and validated hybrid parameters to schema-v2's optional
ALS state. Schema-v1 TF-IDF-only and schema-v2 TF-IDF/ALS artifacts remain
loadable and continue serving their original strategies. The ALS
implementation uses pinned `implicit` 0.7.3 on Python 3.13.7; the slim container
installs only the required `libgomp1` OpenMP runtime.

The SubjectId mapping and purchase-history sets are pseudonymous, linkable
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

Hybrid weights use the `RECOMMENDATION_HYBRID_PERSONALIZED_*_WEIGHT` and
`RECOMMENDATION_HYBRID_SIMILAR_*_WEIGHT` variables. Candidate generation uses
`RECOMMENDATION_HYBRID_CANDIDATE_POOL_MULTIPLIER` and
`RECOMMENDATION_HYBRID_CANDIDATE_POOL_CAP`. Every weight must be finite,
non-negative, and each context's weights must sum to one within `1e-9`.
Component scores use deterministic min-max normalization: empty stays empty;
one/equal positive values become one; one/equal non-positive values become
zero; negative ranges are shifted into 0..1; NaN and infinity are rejected.

`POST /api/v1/models/evaluate` performs deterministic per-subject temporal
evaluation without reading or changing the active artifact. It evaluates
Popularity, implicit ALS, and Hybrid, reports TF-IDF and
frequently-bought-together as
`NotEvaluated`, and atomically writes aggregate JSON and Markdown reports under
`evaluation-reports/`. The report includes explicit Hybrid-minus-ALS metric
deltas without making an acceptance claim. Configure it with `RECOMMENDATION_EVALUATION_K`,
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
