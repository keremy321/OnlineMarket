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

## ERP Integration

Event intake saves:

- processed event,
- batch,
- customer/address/order snapshot,
- order lines,
- four ordered steps.

It returns `202 Accepted` after the transaction commits.

The worker executes one valid step at a time and records every attempt.

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
