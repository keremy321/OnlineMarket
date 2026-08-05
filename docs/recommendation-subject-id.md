# Recommendation SubjectId Contract

`SubjectId` is the stable, opaque technical identifier used for
recommendation-model interactions. It is pseudonymous: it removes the direct
market `CustomerId` from Python contracts, but repeated values remain linkable
and the source customer can be recovered by an operator that possesses both
the derivation key and candidate customer identifiers. It must not be described
as legally or cryptographically anonymous.

## Derivation format

Recommendation.Api is the only component that derives SubjectId.

1. Format `CustomerId` as the lowercase canonical GUID `D` representation.
2. Build the UTF-8 message `<version>:<canonical-customer-id>`.
3. Calculate HMAC-SHA256 with the configured secret key.
4. Encode the 32-byte digest as unpadded Base64 URL text.
5. Prefix it with the version and a period.

For version `v1`, the output is:

```text
v1.<43-character-base64url-hmac>
```

Accepted versions match `v[1-9][0-9]{0,14}`. SubjectId values are at most 64
ASCII characters and match
`^v[1-9][0-9]{0,14}\.[A-Za-z0-9_-]{43}$`.

The version is part of both the HMAC message and output. The same customer,
key, and version always produces the same SubjectId; changing any of them
changes the result. Plain, unkeyed SHA-256 is forbidden.

## Configuration and key storage

Recommendation.Api requires:

```text
RecommendationSubject__Key=<at-least-32-byte-random-secret>
RecommendationSubject__Version=v1
```

Use environment variables or the Recommendation.Api User Secrets store. The
key must not appear in appsettings files, Compose source, database rows, model
artifacts, logs, exception messages, tickets, or browser-visible content.
Python never receives the key or the market CustomerId.

Artifact schema v2 persists deterministic SubjectId mappings and
purchased-product sets so ALS can serve known subjects and exclude prior
purchases. This is pseudonymous, linkable model data rather than anonymous
data. The artifact volume must be access-restricted and must never contain the
direct CustomerId, derivation key, or a customer-to-subject lookup. Offline
evaluation reports contain aggregate counts and metrics only; they do not
persist raw subjects or order-level records.

Normal Recommendation.Api startup fails validation when the key is shorter
than 32 UTF-8 bytes or the version is invalid.

## Ingestion and initial backfill

`OrderConfirmedForRecommendationV1` remains unchanged. Recommendation.Api
derives SubjectId after strict event validation and before persistence. The
order snapshot, SubjectId, items, and ProcessedEvent then commit in the existing
single transaction. Derivation failure prevents the store operation entirely.

The migration deliberately adds nullable `OrderSnapshots.SubjectId`; a secret
must never be embedded in a migration. Application ingestion nevertheless
requires SubjectId for every new successful order.

After applying the migration, call the authenticated operation:

```text
POST /api/v1/recommendations/subjects/backfill
X-Api-Key: <Recommendation.Api key>
```

It scans all order snapshots, derives with the production service, updates only
null SubjectId values, and reports scanned, updated, skipped, and failed counts.
It is idempotent, does not run at startup, and does not modify order items or
ProcessedEvents. Model training rejects incomplete backfill rather than sending
empty subjects to Python.

## Key and version rotation

Changing the key or version changes every subject. A mixed population would
split one customer's historical and new interactions, so rotation must be a
coordinated maintenance operation:

1. Back up RecommendationDb and retain the currently active model artifact.
2. Pause order-event ingestion and model training.
3. Retain the old key/version in the approved secret store for rollback.
4. Configure the new random key and incremented version.
5. Run an approved full historical subject rederivation operation that uses the
   production derivation service and updates all order snapshots atomically in
   controlled batches.
6. Verify every order has the new version prefix and that distinct SubjectId
   count equals distinct CustomerId count.
7. Retrain and validate all subject-dependent models before activation.
8. Resume ingestion only after the database and active model use one version.
9. Retire the old key according to the secret-retention policy after the
   rollback window closes.

The initial missing-only backfill endpoint must not be used as a rotation tool;
a full rotation operation requires separate approval because it rewrites
existing pseudonymous identities.

Rollback before resuming ingestion restores the previous key/version,
RecommendationDb backup, and previous model artifact. After new-version events
have been accepted, rollback also requires quiescing ingestion and restoring or
rederiving the affected order subjects so one customer is not split across
versions.
