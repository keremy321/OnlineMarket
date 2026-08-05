# Recommendation Model Service Development

`Recommendation.Api` remains the only public recommendation API and the owner
of `RecommendationDb`. `Recommendation.ModelService` is an internal Python
service. It has no database driver or connection string and receives explicit,
purpose-limited product and order-product snapshots from `Recommendation.Api`.

## Contract and data boundary

The private training contract contains product content/availability fields and
order baskets with a pseudonymous `SubjectId`, product IDs, and quantities. It
contains no direct market customer ID, name, address, email, phone, payment
data, or other direct customer PII. Python returns
only product IDs, TF-IDF scores, model version, and artifact metadata. Product
display data, price, current activity, and current stock remain outside Python;
`Recommendation.Api` performs a final snapshot availability check and
`OnlineMarket.Web` remains responsible for its final authoritative catalogue
check.

The current order event remains unchanged. `Recommendation.Api` derives a
stable versioned `SubjectId` from its stored market `CustomerId` using
HMAC-SHA256 and sends only that pseudonymous opaque value in Python order
interactions. Python never receives the direct identifier or derivation key.
The derivation, initial backfill, and rotation contract is documented in
`docs/recommendation-subject-id.md`. ALS implementation remains a separate task.

## Configuration

Configure `Recommendation.Api` with environment variables or User Secrets:

```text
Services__RecommendationModelService__BaseAddress=http://127.0.0.1:8085
Services__RecommendationModelService__ApiKey=<LOCAL_ONLY_SECRET>
Services__RecommendationModelService__Timeout=00:00:03
RecommendationSubject__Key=<AT_LEAST_32_BYTE_LOCAL_SECRET>
RecommendationSubject__Version=v1
```

Configure Python with the same local-only key:

```text
RECOMMENDATION_MODEL_API_KEY=<LOCAL_ONLY_SECRET>
RECOMMENDATION_MODEL_ARTIFACT_DIRECTORY=artifacts
```

Never put a real key in `appsettings*.json`, Compose source, logs, issues, or
browser code. Model endpoints use `X-Api-Key`; `GET /health` is intentionally
unauthenticated for local/container health probes.

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

The C# façade makes no automatic HTTP retries. Three consecutive failures open
a 30-second circuit, preventing retry storms. Timeout, connection, unavailable
model, and invalid response outcomes use the deterministic C# content fallback
for user-facing inference.
