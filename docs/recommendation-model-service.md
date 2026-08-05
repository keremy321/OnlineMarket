# Recommendation Model Service Development

`Recommendation.Api` remains the only public recommendation API and the owner
of `RecommendationDb`. `Recommendation.ModelService` is an internal Python
service. It has no database driver or connection string and receives explicit,
purpose-limited product and order-product snapshots from `Recommendation.Api`.

## Contract and data boundary

The private training contract contains product content/availability fields and
order baskets with a pseudonymous `SubjectId`, product IDs, and quantities. It
contains no direct market customer ID, name, address, email, phone, payment
data, or other direct customer PII. Python returns only product IDs, bounded
component/final scores, reason metadata, model version, and artifact metadata. Product
display data, price, current activity, and current stock remain outside Python;
`Recommendation.Api` performs a final snapshot availability check and
`OnlineMarket.Web` remains responsible for its final authoritative catalogue
check.

The current order event remains unchanged. `Recommendation.Api` derives a
stable versioned `SubjectId` from its stored market `CustomerId` using
HMAC-SHA256 and sends only that pseudonymous opaque value in Python order
interactions. Python never receives the direct identifier or derivation key.
The derivation, initial backfill, and rotation contract is documented in
`docs/recommendation-subject-id.md`. The model service aggregates SubjectId and
product interactions and trains a configured implicit-feedback ALS component
alongside TF-IDF. Insufficient ALS data leaves TF-IDF active and reports the ALS
component as `InsufficientData`. Artifact-v3 training also prepares normalized
popularity, sparse directional co-purchase, and sparse TF-IDF-neighbor state.

## Configuration

Configure `Recommendation.Api` with environment variables or User Secrets:

```text
Services__RecommendationModelService__BaseAddress=http://127.0.0.1:8085
Services__RecommendationModelService__ApiKey=<LOCAL_ONLY_SECRET>
Services__RecommendationModelService__Timeout=00:00:03
Services__RecommendationModelService__EvaluationTimeout=00:02:00
RecommendationSubject__Key=<AT_LEAST_32_BYTE_LOCAL_SECRET>
RecommendationSubject__Version=v1
Recommendation__Personalized__ModelStrategy=Als
```

Configure Python with the same local-only key:

```text
RECOMMENDATION_MODEL_API_KEY=<LOCAL_ONLY_SECRET>
RECOMMENDATION_MODEL_ARTIFACT_DIRECTORY=artifacts
RECOMMENDATION_ALS_FACTORS=32
RECOMMENDATION_ALS_REGULARIZATION=0.05
RECOMMENDATION_ALS_ITERATIONS=20
RECOMMENDATION_ALS_ALPHA=20
RECOMMENDATION_ALS_RANDOM_SEED=42
RECOMMENDATION_ALS_DEFAULT_LIMIT=8
RECOMMENDATION_ALS_MAXIMUM_LIMIT=50
RECOMMENDATION_ALS_MINIMUM_SUBJECTS=2
RECOMMENDATION_ALS_MINIMUM_PRODUCTS=2
RECOMMENDATION_ALS_MINIMUM_INTERACTIONS=3
RECOMMENDATION_HYBRID_PERSONALIZED_ALS_WEIGHT=0.50
RECOMMENDATION_HYBRID_PERSONALIZED_CONTENT_WEIGHT=0.20
RECOMMENDATION_HYBRID_PERSONALIZED_ASSOCIATION_WEIGHT=0.15
RECOMMENDATION_HYBRID_PERSONALIZED_POPULARITY_WEIGHT=0.15
RECOMMENDATION_HYBRID_SIMILAR_CONTENT_WEIGHT=0.70
RECOMMENDATION_HYBRID_SIMILAR_ASSOCIATION_WEIGHT=0.20
RECOMMENDATION_HYBRID_SIMILAR_POPULARITY_WEIGHT=0.10
RECOMMENDATION_HYBRID_CANDIDATE_POOL_MULTIPLIER=10
RECOMMENDATION_HYBRID_CANDIDATE_POOL_CAP=500
RECOMMENDATION_EVALUATION_K=5
RECOMMENDATION_EVALUATION_MINIMUM_HISTORICAL_ORDERS=2
RECOMMENDATION_EVALUATION_HOLDOUT_ORDER_COUNT=1
RECOMMENDATION_EVALUATION_EXCLUDE_PREVIOUSLY_PURCHASED=true
RECOMMENDATION_EVALUATION_RANDOM_SEED=42
RECOMMENDATION_EVALUATION_OUTPUT_DIRECTORY=evaluation-reports
RECOMMENDATION_EVALUATION_MAXIMUM_SUBJECTS=0
```

Never put a real key in `appsettings*.json`, Compose source, logs, issues, or
browser code. Model endpoints use `X-Api-Key`; `GET /health` is intentionally
unauthenticated for local/container health probes.

## Hybrid ranking

Personalized ranking weights normalized ALS/content-affinity/directional
association/popularity at `0.50/0.20/0.15/0.15`; Similar uses
content/co-purchase/popularity at `0.70/0.20/0.10`. Content affinity is the
maximum TF-IDF cosine similarity over the subject's training-history products.
Association is directional `support * confidence * log1p(lift)`, and
popularity is based on training-order quantity. Each signal is normalized
independently to 0..1. Missing signals contribute zero and no runtime weight
renormalization occurs.

Candidate generation unions each component's deterministic top pool, removes
duplicates and ineligible products, applies the configured multiplier and
overall cap, then sorts by final score descending and ProductId ascending.
Sparse product neighbors are prepared at training time; no order payload is
stored in the artifact.

## Compose startup

Copy `deploy/.env.example` to the ignored `deploy/.env`, replace both secret
placeholders with different local-only values, then run:

```powershell
docker compose --env-file .\deploy\.env -f .\deploy\docker-compose.development.yml up -d --build
docker compose --env-file .\deploy\.env -f .\deploy\docker-compose.development.yml ps
```

The model health endpoint is `http://127.0.0.1:8085/health`. The Compose named
volume persists versioned artifacts across container replacement. Training is
triggered through authenticated `Recommendation.Api` endpoint
`POST /api/v1/recommendations/recalculate-models`; clients query similar items
through `GET /api/v1/recommendations/similar/{productId}`.
Personalized inference uses
`GET /api/v1/recommendations/customers/{customerId}`; Recommendation.Api derives
the pseudonymous subject internally and calls
`POST /api/v1/models/personalized`.

The base façade configuration remains `Als`, because ALS is the previously
measured serving baseline. `appsettings.Development.json` explicitly selects
`Hybrid`. A requested hybrid on an artifact without all personalized
components falls back to the artifact's ALS strategy without renormalizing
weights. Similar inference uses `HybridSimilar` on schema-v3 artifacts and
retains `PythonTfidf` on schema-v1/v2 artifacts.

Authenticated `POST /api/v1/recommendations/evaluate-models` exports a
purpose-limited chronological snapshot and calls Python
`POST /api/v1/models/evaluate`. It evaluates Popularity, ALS, and Hybrid on a
per-subject newest-order holdout, reports TF-IDF and frequently-bought-together
as `NotEvaluated`, and returns aggregate metrics, explicit Hybrid-minus-ALS
comparisons, and report identifiers.
JSON and Markdown reports are atomically written to the separate
`recommendation_evaluation_reports` volume. Evaluation uses its longer
`EvaluationTimeout`, performs no automatic retry, does not affect the inference
circuit, and never activates or mutates the current serving artifact.

The C# façade makes no automatic HTTP retries. Three consecutive failures open
a 30-second circuit, preventing retry storms. Timeout, connection, unavailable
model, and invalid response outcomes use the deterministic C# content fallback
for user-facing inference.
Personalized failures, cold-start subjects, and TF-IDF-only artifacts use the
deterministic C# category/brand/popularity fallback through the same circuit.

Artifact schema v3 extends the v2 TF-IDF/ALS state with normalized popularity,
sparse directional association/content neighbors, and hybrid parameters.
Schema-v1 and schema-v2 artifacts remain loadable and serve their prior
strategies. Deterministic SubjectId mappings and purchased-product sets are
pseudonymous and linkable; the artifact volume requires restricted access. Direct CustomerId,
the derivation key, customer-to-subject mappings, connection strings, and API
keys remain forbidden in artifacts and evaluation reports.
