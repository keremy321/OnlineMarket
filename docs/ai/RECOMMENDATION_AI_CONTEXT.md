# Online Market Recommendation System — AI Implementation Context

**Version:** 1.0  
**Date:** 2026-08-03  
**Status:** Authoritative AI implementation context  
**Target delivery date:** 2026-08-10

---

## 1. Purpose

This document is the implementation context for AI coding agents working on the Online Market recommendation subsystem.

The recommendation subsystem must support:

1. Popular products
2. Frequently bought together
3. Similar products
4. Personalized recommendations
5. Cart-completion recommendations

This document defines service boundaries, data ownership, event flow, synthetic-data responsibilities, training strategy, model lifecycle, API behavior, fallback behavior, security constraints, testing rules, and implementation order.

---

## 2. Authoritative Architecture

The recommendation subsystem consists of two runtime services and one development/demo data tool.

### Runtime services

| Component | Technology | Responsibility |
|---|---|---|
| `Recommendation.Api` | C# / ASP.NET Core | Event ingestion, `RecommendationDb` ownership, public recommendation endpoints, authorization, rate limiting, fallback, cache, model-service integration |
| `Recommendation.ModelService` | Python / FastAPI | Dataset preparation, model training, evaluation, versioning, artifact management, inference |

### Development and demo tool

| Component | Technology | Responsibility |
|---|---|---|
| `SyntheticData.Producer` | Python CLI or worker | Generate controlled customer, catalogue, stock, cart, checkout, and order activity |

`SyntheticData.Producer` is not a production business service. It exists for bootstrap data, continuous demo activity, replay, and model evaluation.

---

## 3. Data Ownership

### 3.1 Database ownership

| Database | Owner | Recommendation role |
|---|---|---|
| `OnlineMarketDb` | `OnlineMarket.Web` | Source of truth for customers, catalogue, carts, checkout, orders, and order items |
| `RecommendationDb` | `Recommendation.Api` | Product snapshots, order snapshots, processed events, recommendation outputs, run metadata, and model-related projections |
| `IntegrationDb` | `ErpIntegration.Api` | ERP processing state only; not a recommendation training source |
| `MockErpDb` | `MockErp.Api` | ERP simulation only; not a recommendation training source |

### 3.2 Hard ownership rules

1. `Recommendation.Api` is the only owner of `RecommendationDb`.
2. `SyntheticData.Producer` must never insert directly into `RecommendationDb`.
3. Python model code must not own EF migrations or modify database schemas.
4. Python must not query `OnlineMarketDb`, `IntegrationDb`, or `MockErpDb` directly.
5. No cross-database joins, foreign keys, or shared DbContext.
6. Recommendation data must not contain personal, address, payment, or ERP-sensitive information.
7. Current `RecommendationDb` entities, EF configurations, migrations, and model snapshots remain authoritative unless a separately approved schema task changes them.

---

## 4. End-to-End Data Flow

```text
SyntheticData.Producer
    → OnlineMarket.Web
    → OnlineMarketDb
    → Transactional Outbox
    → Recommendation.Api
    → RecommendationDb
    → Recommendation.ModelService
    → Versioned model artifacts
    → Recommendation.Api
    → OnlineMarket.Web
    → UI
```

Detailed flow:

1. The data producer creates customer and shopping activity through Online Market.
2. Online Market persists business data using its own application rules.
3. Product and confirmed-order changes create transactional Outbox messages.
4. Online Market delivers product and order events to `Recommendation.Api`.
5. `Recommendation.Api` validates and projects events into `RecommendationDb`.
6. Training is triggered by time or data-volume thresholds.
7. `Recommendation.Api` exports purpose-limited training data with a versioned pseudonymous `SubjectId` to the Python model service.
8. Python trains and evaluates models.
9. A successful model produces versioned artifacts.
10. The active model changes only after validation succeeds.
11. Public recommendation requests go through `Recommendation.Api`.
12. Python returns product IDs, scores, model version, and reason metadata only.
13. Online Market resolves current name, image, price, active state, and stock from `OnlineMarketDb`.

---

## 5. Source Events

## 5.1 Product event

Event name:

```text
ProductSnapshotChangedV1
```

Expected fields:

```text
EventId
OccurredAtUtc
CorrelationId
ProductId
Sku
Name
CategoryId
ParentCategoryId?
BrandId
Price
NetContent
UnitType
IsActive
IsInStock
SourceUpdatedAtUtc
```

Required behavior:

- Strict JSON parsing
- Unknown properties rejected
- Canonical SHA-256 payload hashing
- Same `EventId` and same hash: successful replay
- Same `EventId` and different hash: `409 Idempotency.PayloadConflict`
- Older `SourceUpdatedAtUtc` must not overwrite newer product data
- Product snapshot and `ProcessedEvent` must commit atomically
- No incomplete placeholder product snapshots

## 5.2 Confirmed order event

Event name:

```text
OrderConfirmedForRecommendationV1
```

Allowed fields only:

```text
EventId
OccurredAtUtc
CorrelationId
OrderId
OrderNumber
CustomerId
Items[] {
    ProductId
    Quantity
}
```

Forbidden fields include:

```text
Email
Phone
Address
Customer name
Price
VAT
Order total
Payment method
Card information
ERP customer code
ERP payload
```

Required behavior:

- Strict JSON parsing
- Unknown properties rejected
- Duplicate `ProductId` lines rejected
- Quantity must be positive
- Every referenced product snapshot must already exist
- Missing product snapshot:
  - no order snapshot written
  - no order items written
  - no processed event written
  - return `409`
  - error code: `Recommendation.ProductSnapshotMissing`
  - `retryable = true`
- Order snapshot, items, and processed event commit in one transaction
- `TotalQuantity` and `DistinctProductCount` are calculated by the consumer
- Same `EventId` and same hash: successful replay
- Same `EventId` and different hash: non-retryable conflict
- Concurrent replay must create at most one logical result

---

## 6. Synthetic Data Producer

## 6.1 Responsibility

Yudum owns the synthetic-data and analysis work.

The producer generates:

- categories
- brands
- products
- initial stock
- customers
- customer addresses
- carts
- cart items
- completed orders
- order items
- stock movements
- continuous shopping activity

It must not write directly to:

- `RecommendationDb`
- `IntegrationDb`
- Mock ERP business tables that should be created through integration

ERP customer, order, stock-movement, and accounting records should be created through the real ERP integration flow where practical.

## 6.2 Operating modes

### Bootstrap mode

Generates historical activity for initial training.

Recommended minimum:

```text
80–120 products
40–60 customers
350–600 orders
3–8 distinct products per order
3–6 months of timestamps
```

Preferred stronger demo scale when local hardware allows:

```text
100–150 products
250–500 customers
3,000–10,000 orders
```

### Stream mode

Continuously creates new shopping activity while the system is running.

Recommended demo rate:

```text
1–10 orders per minute
```

Stream activity must use the real Online Market flow:

```text
Select customer
→ create or reuse cart
→ add products
→ checkout
→ create order and outbox events
→ Recommendation and ERP flows continue automatically
```

### Replay mode

Replays a deterministic dataset for:

- end-to-end tests
- demo preparation
- bug reproduction
- repeatable model comparison

## 6.3 Behavioral patterns

The data must not be uniformly random.

Suggested personas:

- Breakfast shopping
- Pasta and sauces
- Coffee consumer
- Snacks and beverages
- Cleaning products
- Baby products
- Student
- Office
- Gaming/technology

Suggested bundles:

```text
Coffee → Coffee filter
Pasta → Pasta sauce
Chips → Soft drink
Baby diapers → Wet wipes
Laundry detergent → Fabric softener
Laptop → Laptop bag
Gaming mouse → Mousepad
Phone → Case
```

Suggested probability mix:

```text
60% persona preference
20% global popularity
15% bundle rule
5% random noise
```

These percentages must be configurable.

## 6.4 Determinism and data quality

Requirements:

- fixed random seed
- same config and seed produce the same bootstrap dataset
- no empty carts
- no zero or negative quantities
- no unknown product references
- no duplicate order identifiers
- no duplicate product lines inside one order
- generated dataset summary
- data-quality report

Suggested outputs:

```text
data-generator-config.yaml
segment-definitions.json
bundle-rules.json
dataset-summary.json
data-quality-report.md
demo-orders.ndjson
```

---

## 7. Continuous Data and Retraining

Continuous growth does not mean full retraining after every order.

## 7.1 Lightweight updates

The following can be updated incrementally:

- product sold quantity
- order count
- last purchase timestamp
- category preference counters
- brand preference counters
- co-occurrence counters
- orders received since last training run

## 7.2 Periodic training

| Model | Demo trigger | Realistic production-style description |
|---|---|---|
| Popularity | every 10–25 orders | every 5–15 minutes |
| FP-Growth | every 25–50 orders | hourly or data-threshold based |
| TF-IDF | catalogue change | after meaningful catalogue updates |
| Implicit ALS | every 100–250 orders | daily or after sufficient interactions |
| Hybrid | after valid submodels | after every successful model set |

Rules:

1. Only one full training run may execute at a time.
2. Existing inference continues while training runs.
3. A failed training run must not replace the current model.
4. Training runs must record status, parameters, counts, metrics, duration, and failure details.
5. Short demo thresholds must be documented as accelerated demo settings, not production recommendations.

Suggested run states:

```text
Pending
Running
Succeeded
Failed
Cancelled
```

---

## 8. Models

## 8.1 Popularity baseline

Purpose:

- anonymous users
- new users
- insufficient history
- Python service unavailable
- no active personalized model

Possible score:

```text
PopularityScore =
    0.50 × Normalize(SoldQuantity)
  + 0.30 × Normalize(OrderCount)
  + 0.20 × RecencyScore
```

Popularity fallback must be available from `Recommendation.Api` without requiring Python inference.

## 8.2 FP-Growth and association rules

Use cases:

- frequently bought together
- cart completion

Metrics:

- support
- confidence
- lift
- co-occurrence count

Possible score:

```text
AssociationScore =
    Confidence
  × log(1 + Lift)
  × SupportWeight
```

Rules:

- remove products already in the cart
- remove inactive products
- remove out-of-stock products
- filter rules below configured support
- treat `A → B` and `B → A` as separate rules

## 8.3 TF-IDF content similarity

Possible features:

```text
Name
CategoryId
ParentCategoryId
BrandId
UnitType
NetContent bucket
Price bucket
Description text when available
```

Process:

1. Build product feature text
2. Fit TF-IDF vectorizer
3. Calculate cosine similarity
4. Remove the input product itself
5. Filter inactive and out-of-stock products

Primary use:

- similar products
- cold-start products
- sparse-purchase products

## 8.4 Implicit ALS

Use implicit purchase behavior rather than explicit ratings.

Suggested interaction weight:

```text
InteractionWeight =
    1
  + log(1 + TotalQuantity)
  + log(1 + max(RepeatPurchaseCount - 1, 0))
```

The MVP deliberately defers recency weighting. It aggregates deterministic
`SubjectId + ProductId` pairs across confirmed orders, uses a configured random
seed and single-threaded ALS/BLAS execution, and reports insufficient data
without failing the TF-IDF component.

Input:

```text
SubjectId
ProductId
Quantity
OccurredAtUtc
```

Output:

```text
SubjectId
RecommendedProductId
Score
ModelVersion
```

Cold-start fallback:

- category affinity
- brand affinity
- popularity
- content similarity

## 8.5 Hybrid ranking

Recommended personalized score:

```text
FinalScore =
    0.50 × ALS
  + 0.20 × ContentAffinity
  + 0.15 × Association
  + 0.15 × Popularity
```

Recommended cart score:

```text
FinalScore =
    0.65 × Association
  + 0.20 × Popularity
  + 0.15 × ContentSimilarity
```

Recommended similar-product score:

```text
FinalScore =
    0.70 × ContentSimilarity
  + 0.20 × CoPurchaseSimilarity
  + 0.10 × Popularity
```

Weights must be configuration or model parameters, not duplicated magic numbers across files.

---

## 9. Python Model Service Contract

Internal endpoints:

```text
POST /internal/v1/models/train
GET  /internal/v1/models/current
POST /internal/v1/predict/personalized
POST /internal/v1/predict/cart
POST /internal/v1/predict/similar
POST /internal/v1/predict/frequently-bought-together
GET  /health/live
GET  /health/ready
```

The implemented route names remain consistent with the existing service:

```text
POST /api/v1/models/train
POST /api/v1/models/similar
POST /api/v1/models/personalized
GET  /api/v1/models/current
```

The Python service must not be called directly by the browser.

## 9.1 Training payload

Only purpose-limited recommendation fields may be sent. `SubjectId` is
pseudonymous and linkable across interactions; it must not be described as
legally or cryptographically anonymous.

Products:

```text
ProductId
Name
CategoryId
ParentCategoryId
BrandId
Price
NetContent
UnitType
IsActive
IsInStock
```

Interactions:

```text
OrderId
SubjectId
OccurredAtUtc
Items[] {
    ProductId
    Quantity
}
```

Transport:

- first version: gzip JSON
- optional later: NDJSON or Parquet

## 9.2 Inference response

Example:

```json
{
  "modelVersion": "hybrid-20260805-001",
  "strategy": "hybrid",
  "recommendations": [
    {
      "productId": "00000000-0000-0000-0000-000000000000",
      "score": 0.873421,
      "confidence": 0.81,
      "reasonCode": "FrequentlyBoughtWithCart",
      "reasonText": "Frequently purchased with products in the cart."
    }
  ]
}
```

Python must not return authoritative:

- product name
- current price
- image URL
- exact stock quantity

Online Market owns those values.

---

## 10. Model Artifacts

Suggested structure:

```text
artifacts/
└── hybrid-20260805-001/
    ├── metadata.json
    ├── metrics.json
    ├── als_model.npz
    ├── user_mapping.json
    ├── item_mapping.json
    ├── tfidf_vectorizer.pkl
    ├── item_matrix.npz
    ├── association_rules.parquet
    └── checksums.json
```

Activation process:

1. Write new artifacts to a temporary directory.
2. Validate all files and checksums.
3. Complete offline evaluation.
4. Verify minimum acceptance criteria.
5. Atomically publish the artifact directory.
6. Atomically update `current.json`.
7. Keep the previous model for rollback.

Artifact schema v3 stores TF-IDF, optional `implicit` ALS, normalized
popularity, sparse directional co-purchase/content neighbors, hybrid weights,
deterministic SubjectId/product mappings, and purchased-product index sets. The
loader continues accepting schema-v1 TF-IDF-only and schema-v2 TF-IDF/ALS
joblib artifacts; old artifacts retain their prior inference strategies.
Every temporary artifact is deserialized and dimension-validated before its
atomic rename and in-memory activation.

The SubjectId mapping and purchase-history sets are pseudonymous, linkable
model data. Artifacts must never contain the direct market CustomerId, the
derivation key, a customer-to-subject mapping, or secrets, and access to the
artifact volume must be restricted accordingly.

Do not overwrite the active model in place.

---

## 11. Evaluation

Use temporal splitting:

```text
Older orders → training
Newest orders → validation/test
```

The deterministic MVP groups by opaque SubjectId, orders each subject's
history by `(OccurredAtUtc, OrderId)`, holds out the configured newest order(s),
and trains only from strictly older orders. Subjects without enough history,
usable training interactions, or candidate test labels are excluded with
explicit aggregate reason counts. Previously purchased products are excluded
from candidates when configured.

The MVP evaluates Popularity, implicit ALS, and Hybrid using the same split.
It reports explicit Hybrid-minus-ALS precision, recall, hit-rate, NDCG, and
coverage deltas without assuming Hybrid is superior. TF-IDF Similar Products and
frequently-bought-together use different query/label protocols and therefore
must be reported as `NotEvaluated` with explicit reasons rather than assigned
fabricated personalized-ranking metrics. Evaluation trains isolated in-memory
models, does not activate or overwrite the serving artifact, and atomically
publishes aggregate JSON and Markdown reports.

Required metrics:

- Precision@5
- Recall@5
- HitRate@5
- NDCG@5
- Catalogue coverage
- Diversity
- Cold-start coverage
- Training duration
- Average inference latency
- P95 inference latency

For this MVP, Precision@K is hits divided by K, Recall@K is hits divided by the
number of held-out candidate labels, HitRate@K is one when at least one label is
hit, and NDCG@K uses binary relevance with ideal DCG truncated to the smaller
of K and the label count. Metrics are macro-averaged across eligible subjects.
Catalogue coverage is the distinct recommended candidate products divided by
the candidate catalogue size. ALS reports both known-model coverage and
coverage including its deterministic Popularity fallback. Training duration,
average latency, and nearest-rank P95 latency are measured values and are the
only intentionally runtime-dependent report fields.

Models to compare:

| Model | Role |
|---|---|
| Popularity | baseline |
| FP-Growth | basket and association |
| TF-IDF | similar products |
| ALS | personalization |
| Hybrid | final ranking |

Final reports must use actual measured outputs. Do not invent metric values.

---

## 12. Public Recommendation API

Planned public endpoints:

```text
GET  /api/v1/recommendations/popular
GET  /api/v1/recommendations/products/{id}/frequently-bought-together
GET  /api/v1/recommendations/products/{id}/similar
GET  /api/v1/recommendations/customers/{id}
POST /api/v1/recommendations/cart
POST /api/v1/recommendations/recalculate
```

Public responses should return recommendation metadata and product IDs.

Filtering rules:

- active products only
- in-stock products only
- no duplicate product IDs
- remove products already in the cart
- optionally reduce products already purchased by the customer
- normalize output scores to `0..1`
- return strategy and model version when useful

---

## 13. Fallback and Resilience

When Python inference fails:

```text
Personalized → preference scores + popularity
Cart → persisted affinities + popularity
Similar → persisted similarities or category/brand fallback
Popular → RecommendationDb popularity
```

Rules:

- short HTTP timeout
- no SQL transaction held during Python HTTP calls
- circuit breaker permitted
- Python failure must not break checkout or catalogue browsing
- prefer fallback over empty results
- expose the strategy used:

```text
hybrid
popularity_fallback
content_fallback
association_fallback
personalized_preference_fallback
```

---

## 14. Security and Privacy

Never send or persist in the recommendation subsystem:

- email
- phone
- address
- customer name
- payment method
- card data
- ERP customer code
- API keys
- connection strings

Rules:

- internal C# ↔ Python calls require an API key
- constant-time key comparison
- secrets from User Secrets or environment variables
- no API keys in logs
- no personal payload logging
- no personal data in model artifacts
- direct market `CustomerId`, the SubjectId derivation key, and customer-to-subject mappings are forbidden in Python requests, artifacts, and logs
- `SubjectId` is an opaque, purpose-limited pseudonymous technical identifier derived only by `Recommendation.Api`
- CORS disabled unless explicitly required
- train/recalculate endpoints must be internal or admin-protected

---

## 15. Testing Rules

## 15.1 Recommendation.Api

Use real SQL Server with migrations.

Required coverage:

- product-event idempotency
- stale product events
- order-event idempotency
- missing product rollback
- duplicate product rejection
- transaction atomicity
- concurrent EventId intake
- recalculation lock
- Python timeout fallback
- malformed Python response fallback
- API-key validation
- unknown JSON property rejection
- no schema or migration drift

## 15.2 Python

Required coverage:

- deterministic preprocessing
- empty dataset behavior
- single-user/single-product edge cases
- FP-Growth support/confidence/lift
- TF-IDF self-product exclusion
- known-user ALS inference
- unknown-user fallback
- hybrid score normalization
- artifact save/load parity
- model rollback
- temporal split correctness
- metric calculation
- deterministic training when seed and data are unchanged

## 15.3 End-to-end

Required flow:

```text
SyntheticData.Producer
→ OnlineMarket checkout
→ Outbox
→ Recommendation ingestion
→ RecommendationDb
→ Python training
→ Model activation
→ Recommendation query
→ Online Market UI
```

The demo must show:

- data counts increasing
- recommendation events being processed
- a new model version being activated
- recommendations changing after new data and retraining
- fallback when the Python service is unavailable

---

## 16. Implementation Order

Recommended sequence:

1. Recommendation product and order event ingestion
2. Bootstrap data producer
3. Data-quality report
4. Popularity baseline
5. FP-Growth
6. TF-IDF similarity
7. Model artifact and versioning
8. Implicit ALS
9. Temporal evaluation
10. Hybrid ranker
11. Python internal API
12. C# typed model client
13. Fallback and cache
14. Stream producer
15. Automatic retraining trigger
16. Online Market UI integration
17. End-to-end demo and report

Priority under time pressure:

```text
Ingestion
→ Popularity
→ FP-Growth
→ TF-IDF
→ ALS
→ Hybrid
```

Do not sacrifice reliable event ingestion, fallback, or UI integration to add another model.

---

## 17. AI Coding-Agent Rules

Before editing:

1. Read:
   - `AGENTS.md`
   - `docs/ai/ARCHITECTURE.md`
   - `docs/ai/DATA_AND_CONTRACTS.md`
   - `docs/ai/DATABASE.md`
   - this document
2. Inspect the current branch and current migrations.
3. Treat the current repository as authoritative.
4. Report any real schema conflict before changing entities or migrations.

Do not:

- redesign current databases without approval
- change migrations as part of model implementation
- create a shared database or shared DbContext
- add Generic Repository or Generic Service
- add MediatR or AutoMapper without an approved need
- create direct Python database ownership
- bypass Online Market events by directly seeding RecommendationDb
- expose Python endpoints publicly
- log personal data or secrets
- return stale price or stock from model artifacts
- retrain the complete model after every event
- add Kafka, RabbitMQ, Kubernetes, or a feature store
- implement deep learning before the approved classical models work
- commit or push unless explicitly asked

When implementing a task:

1. Keep scope focused.
2. Preserve current contracts.
3. Use strict validation.
4. Use explicit transaction boundaries.
5. Add focused tests.
6. Run focused build/tests.
7. Run full solution build/tests.
8. Report:
   - changed files
   - endpoints
   - contracts
   - transaction boundaries
   - model behavior
   - fallback behavior
   - focused and full test results
   - remaining risks

---

## 18. Definition of Done

The recommendation subsystem is complete when:

1. Product and order events are ingested idempotently.
2. `RecommendationDb` is filled through events, not direct external inserts.
3. Bootstrap and stream data generation work.
4. Popularity, FP-Growth, and TF-IDF work.
5. ALS is included when completed and validated.
6. Models are versioned and measured.
7. Failed training does not replace the active model.
8. Python downtime returns a valid C# fallback.
9. Only active and in-stock products are recommended.
10. No personal or financial data enters recommendation payloads or artifacts.
11. Metrics come from real evaluation runs.
12. Online Market displays recommendations on home, product-detail, and cart pages.
13. The end-to-end demo shows new orders reaching `RecommendationDb`.
14. The demo shows retraining and a model-version change.
15. The demo shows recommendation results changing as data grows.

---

## 19. Explicitly Out of Scope

- per-event neural online learning
- Kafka or RabbitMQ
- distributed training
- feature store
- GPU requirement
- deep-learning recommender
- transformer recommender
- reinforcement learning
- production A/B-testing platform
- production-scale model serving
- personal-data profiling
- direct external inserts into `RecommendationDb`
- Python access to Online Market or ERP databases
- legal or production compliance claims

---

## 20. Final Architecture Summary

```text
Yudum generates source market and ERP demo data.
RecommendationDb is never filled directly by the data producer.
OnlineMarket emits product and confirmed-order events.
Recommendation.Api is the sole owner of RecommendationDb.
Recommendation.ModelService owns training, evaluation, artifacts, and inference.
Data grows continuously.
Lightweight projections update frequently.
Heavy models retrain periodically.
A new model is activated only after validation.
Python failure does not stop Online Market or fallback recommendations.
```
