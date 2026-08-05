# Architecture Summary

## System Style

The project uses a hybrid architecture:

- `OnlineMarket.Web` is a modular monolith.
- Recommendation, ERP Integration, and Mock ERP are separate services.

All four applications can run independently.

## Database Ownership

| Application | Database |
|---|---|
| `OnlineMarket.Web` | `OnlineMarketDb` |
| `Recommendation.Api` | `RecommendationDb` |
| `ErpIntegration.Api` | `IntegrationDb` |
| `MockErp.Api` | `MockErpDb` |

Each application uses only its own database and `DbContext`.

## Internal Code Direction

```text
Controller / MVC Surface
        -> Application
            -> Domain

Infrastructure implements external and persistence concerns.
```

- Domain: entities, value objects, enums, pure rules.
- Application: workflows, services, ports, application models.
- Infrastructure: EF Core, SQL, HTTP clients, background workers.
- Web/API: controllers, ViewModels, request/response contracts.

## Online Market Modules

- Identity
- Catalog
- Inventory
- Cart
- Ordering
- Payments
- Recommendations adapter
- ERP Integration adapter

Admin UI is an MVC Area, not a separate domain module.

The existing Online Market CLR entity/configuration namespaces remain in their
pre-reconciliation folders to preserve compatibility with the shared initial
migration. New integration-event DTOs and canonical serialization are owned by
`Common/Messaging`, while SQL-specific stock mutation, order-number allocation,
and outbox persistence stay in Infrastructure behind application interfaces.
A physical module move is deferred because a cosmetic move provides no runtime
benefit and creates avoidable migration-history risk.

## Checkout Transaction

Inside one SQL transaction:

1. Validate authenticated customer, cart, and selected address.
2. Reload current products, price, VAT, and activity.
3. Run payment simulation.
4. Atomically decrease stock.
5. Create stock movements.
6. Create order, address snapshot, item snapshots, and payment.
7. Convert the cart.
8. Write the two order outbox events.
9. Commit.

No HTTP request is allowed inside this transaction.

`OrderReadyForErpV1` carries the non-sensitive payment method together with
customer, delivery-address, total, and item snapshots.

Order numbers are allocated from the SQL Server
`OnlineMarketOrderNumberSequence`; sequence gaps are allowed, while the unique
order-number index remains the final database guarantee.

## Database Lifecycle

Ordinary application startup does not apply migrations, create schema, seed
catalogue data, or delete development data. Schema changes use the controlled
database scripts. Catalogue/role/admin seeding is an explicit Development-only
command that assumes all migrations are already applied.

## Outbox

The outbox worker:

1. Claims a small batch in a short transaction.
2. Commits the claim.
3. Sends HTTP outside the transaction.
4. Updates success, retry, or permanent failure in a second short transaction.

Two workers must not claim the same message.

## Recommendation Event Intake

- Validate request and API key.
- Check `EventId` and payload hash.
- Save the snapshot and processed-event record atomically.
- Do not run full recalculation for every event.
- Reject an order event when a referenced product snapshot is missing.
- Ignore stale product updates using `SourceUpdatedAtUtc`.
- Allow only one full recalculation at a time.
- Keep previous valid results if recalculation fails.

## Recommendation Model Service

- `Recommendation.Api` remains the public façade and owns all RecommendationDb
  access.
- The internal Python `Recommendation.ModelService` receives explicit,
  authenticated, PII-free training snapshots over HTTP and never accesses a
  database directly.
- The first versioned artifact is deterministic TF-IDF product-content
  similarity. C# popularity, affinity, cart completion, and deterministic
  content similarity remain available for hybrid inputs and fallback.
- `Recommendation.Api` derives a stable pseudonymous `SubjectId` with
  versioned HMAC-SHA256 and exports only that opaque value for order
  interactions. Direct market `CustomerId` and the derivation key never enter
  Python contracts, artifacts, or logs.

## ERP Integration

Event intake saves:

- processed event,
- batch,
- customer, delivery-address, order, payment-method, and total snapshot,
- order lines,
- four ordered steps.

It returns `202 Accepted` after the transaction commits.

The worker executes one valid step at a time and records every attempt.

The four steps remain:

1. Ensure customer.
2. Create ERP order and immutable delivery-address snapshot.
3. Create ERP stock movements.
4. Create accounting voucher header and account-coded lines.

## Mock ERP Write Model

### Ensure customer

- Reuse one ERP customer for each external market `CustomerId`.
- Store ERP customer code, name, email, phone, and current address.
- Do not store tax/identity number, open-account balance, or supplier type.

### Create order

- Persist order and line snapshots.
- Persist the selected delivery address as an order-level immutable snapshot.
- Persist the non-sensitive payment-method enum.
- V1 orders are created as completed sales documents; draft/approval workflow
  and dispatch notes are not modelled.

### Create stock movement

- Use a Mock ERP-owned stock balance independent from market stock.
- The stock card keeps SKU, product name, unit type, net content, quantity,
  and reorder level.
- Use one stock movement per order and product.
- V1 assumes one warehouse and has no warehouse/location table.

### Create accounting entry

Create one sales voucher header and three deterministic lines:

1. Account `120` — Customers/Receivables:
   debit `GrandTotal`, direct ERP customer reference.
2. Account `600` — Domestic Sales:
   credit `Subtotal`.
3. Account `391` — VAT Payable:
   credit `VatTotal`.

The voucher stores type, date, description, payment method, direct customer
reference, total debit, and total credit. Total debit must equal total credit.

This is a project simulation and must not be represented as a legal accounting
or real Uyumsoft contract.

## HTTP and Retry

Use:

- short HTTP timeout,
- limited fast retry,
- circuit breaker,
- durable retry in outbox or integration tables.

Do not rely only on in-memory HTTP retry.

## Security

- ASP.NET Core Identity and server-side authorisation
- `Customer` and `Admin` roles
- Anti-forgery on state-changing MVC forms
- Per-service API keys
- Constant-time key comparison
- Rate limiting
- Secure, HttpOnly, SameSite cookies
- HTTPS and HSTS in production
- CORS disabled by default
- Masked structured logs
- Secrets in User Secrets or environment variables

Recommendation or ERP unavailability must not stop market checkout.
