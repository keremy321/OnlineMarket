# Data and Contracts Summary

## Global Data Rules

- Business IDs: `Guid`
- Money: `decimal(18,2)`
- VAT: `decimal(5,2)`
- Recommendation score: `decimal(12,6)`
- Product content amount: `decimal(12,3)`
- UTC timestamp: `datetime2(3)`
- Currency: `TRY`
- Country: `TR`
- Payload hash: SHA-256
- Mutable aggregates may use `rowversion`

Use real SQL Server tests for transactions, constraints, filtered indexes, and concurrency.

## Main Database Areas

### OnlineMarketDb

- Identity
- Customers and addresses
- Categories, brands, products
- Stocks and stock movements
- Carts and cart items
- Orders, order addresses, order items, payments
- Outbox messages

Important rules:

- one active cart per customer,
- one default active address per customer,
- one stock row per product,
- no negative stock,
- order address and item data are snapshots,
- one order per source cart,
- order and outbox records commit together.

### RecommendationDb

- Product snapshots
- Order snapshots and items
- Product affinities
- Product similarities
- Product popularity
- Customer preference scores
- Processed events
- Recommendation runs

Do not store address, phone, payment, or other unnecessary personal data.

### IntegrationDb

- Processed events
- Integration batches
- Order/customer/address snapshots
- Integration lines
- Steps
- Attempts
- ERP customer links

The snapshot must contain enough data to retry without calling Online Market.

### MockErpDb

- ERP customers
- ERP orders and lines
- ERP stocks and movements
- ERP accounting entries
- Idempotency records

## Events

### ProductSnapshotChangedV1

Carries:

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

Emit on product changes and stock availability transition between zero and positive.

### OrderConfirmedForRecommendationV1

Carries:

```text
EventId
OccurredAtUtc
CorrelationId
OrderId
OrderNumber
CustomerId
Items[] { ProductId, Quantity }
```

Must not contain address, email, phone, payment, or financial details.

### OrderReadyForErpV1

Carries order, customer, selected address, totals, and item snapshots required for durable ERP retry.

Each `ProductId` may appear at most once in `Items`. The ERP Integration
consumer rejects duplicate product lines during request validation; it does
not merge or normalise them.

## Event Idempotency

- Same `EventId` and same payload hash: successful replay.
- Same `EventId` and different hash: conflict, no processing.
- Missing product snapshot: HTTP 409 with retryable application error.
- Recalculation already running: HTTP 409, not retryable.
- Idempotency payload conflict: HTTP 409, not retryable.

Retry decisions must use the application error code, not only the HTTP status.

## Recommendation Endpoints

```text
POST /api/v1/events/products
POST /api/v1/events/orders
GET  /api/v1/recommendations/popular
GET  /api/v1/recommendations/products/{id}/frequently-bought-together
GET  /api/v1/recommendations/products/{id}/similar
GET  /api/v1/recommendations/customers/{id}
POST /api/v1/recommendations/cart
POST /api/v1/recommendations/recalculate
```

## ERP Integration Endpoints

```text
POST /api/v1/integration/orders
GET  /api/v1/integration/orders/{orderId}
POST /api/v1/integration/orders/{orderId}/retry
GET  /api/v1/integration/customers/{customerId}/orders
GET  /api/v1/integration/jobs
```

## Mock ERP Endpoints

```text
POST /api/v1/customers/ensure
POST /api/v1/orders
POST /api/v1/stock-movements
POST /api/v1/accounting-entries
GET  /api/v1/customers/{erpCustomerCode}/orders
```

State-changing Mock ERP requests require a stable `Idempotency-Key`.
