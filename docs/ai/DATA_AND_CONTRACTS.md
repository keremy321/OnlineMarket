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

`Stocks.ReorderLevel` is the product-specific critical-stock threshold used by
low-stock queries. It is not a warehouse/location model.

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
- Order/customer/delivery-address snapshots
- Payment-method snapshot
- Integration lines
- Steps
- Attempts
- ERP customer links

The snapshot must contain enough data to retry without calling Online Market.

### MockErpDb

- ERP customers
- ERP orders, delivery-address snapshots, and lines
- ERP stock cards and movements
- ERP accounting voucher headers and account-coded lines
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
Description?
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

### Recommendation.ModelService internal V1

`Recommendation.Api` sends an authenticated training request containing model
version/correlation data, product content snapshots, and order interactions
with a versioned pseudonymous `SubjectId`, product IDs, and quantities. It must
not send direct market `CustomerId` or customer PII.
Python returns recommendation-owned IDs/scores and model artifact metadata.
The internal endpoints are:

```text
GET  /health
POST /api/v1/models/train
POST /api/v1/models/similar
POST /api/v1/models/personalized
POST /api/v1/models/evaluate
GET  /api/v1/models/current
```

`Recommendation.Api` exposes the authenticated façade endpoints:

```text
POST /api/v1/recommendations/recalculate-models
GET  /api/v1/recommendations/similar/{productId}?limit={value}
GET  /api/v1/recommendations/customers/{customerId}?limit={value}
POST /api/v1/recommendations/evaluate-models
```

The stored market `CustomerId` remains internal to Recommendation.Api. Python
treats `SubjectId` as an opaque string; it never receives the derivation key or
the direct identifier.

Personalized model requests contain `subjectId`, a capped `limit`,
`excludePreviouslyPurchased`, and the configured `Als` or `Hybrid` strategy.
Hybrid responses add ALS, content-affinity, association, popularity, and final
diagnostic scores. Responses contain model version, strategy, and
recommendation-owned product IDs/scores/reason metadata only. Unknown subjects
return an explicit cold-start result so Recommendation.Api can use its local
preference/popularity fallback.

Evaluation requests contain purpose-limited product content/availability
snapshots, catalogue/candidate product IDs, and chronological order interactions
with `OrderId`, opaque `SubjectId`, `OccurredAtUtc`, product ID, and quantity.
Responses contain aggregate dataset/split counts, Popularity, ALS, and Hybrid
metrics and comparisons, explicit NotEvaluated reasons for unsupported
protocols, an input hash, and report identifiers. They never return raw
subjects or order-level payloads.

### OrderReadyForErpV1

Carries:

```text
EventId
OccurredAtUtc
CorrelationId
OrderId
OrderNumber
OrderPlacedAtUtc
PaymentMethod
Customer {
  CustomerId
  FirstName
  LastName
  Email
}
Address {
  RecipientName
  PhoneNumber
  AddressLine1
  AddressLine2?
  District
  City
  PostalCode?
  CountryCode
}
Totals {
  Subtotal
  VatTotal
  GrandTotal
  Currency
}
Items[] {
  ProductId
  Sku
  ProductName
  Quantity
  UnitPrice
  VatRate
  NetLineAmount
  VatAmount
  LineTotal
}
```

`PaymentMethod` is a non-sensitive enum snapshot with the same numeric values
as the Online Market contract:

```text
1 = CashSimulation
2 = CardSimulation
3 = TransferSimulation
```

Each `ProductId` may appear at most once in `Items`. The ERP Integration
consumer rejects duplicate product lines during request validation; it does
not merge or normalise them.

Do not include card number, CVV, expiry date, payment token, or provider
credentials.

## ERP Data-Parity Rules

### Customer/current-account card

The Mock ERP customer record contains:

- ERP customer code,
- external market customer ID,
- name,
- email,
- phone,
- current address.

V1 deliberately excludes tax/identity number, open-account balance, and
supplier/current-account type.

### ERP order

The ERP order stores:

- ERP and market order numbers,
- direct ERP customer relation,
- order date,
- payment method,
- line price/VAT snapshots,
- subtotal, VAT total, grand total, and currency,
- an immutable delivery-address snapshot.

V1 does not model draft/approval/cancellation workflow or dispatch notes.

### ERP stock card

The ERP stock card stores:

- external product ID,
- SKU,
- product name,
- unit type,
- net content,
- current quantity,
- reorder level.

Market stock and ERP stock are independent balances. V1 assumes one warehouse.

### ERP sales accounting voucher

One accounting voucher is created per ERP order. It contains:

- voucher number,
- voucher type `SalesInvoice`,
- entry date,
- direct ERP customer reference,
- payment method,
- description,
- total debit and total credit,
- currency.

It contains these V1 project lines:

```text
120 Customers/Receivables  Debit  = GrandTotal
600 Domestic Sales        Credit = Subtotal
391 VAT Payable           Credit = VatTotal, only when VatTotal > 0
```

Accounts `120` and `600` are always created. Account `391` is created only
when `VatTotal` is positive; a zero-value accounting line is never created.
The sum of debit lines must equal the sum of credit lines. This is a project
simulation, not a legal accounting or real Uyumsoft contract.

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

`POST /api/v1/orders` must persist the ERP order, its lines, its payment
method, and its delivery-address snapshot atomically.

`POST /api/v1/accounting-entries` must persist the voucher header, all
account-coded lines, and its idempotency record atomically.
