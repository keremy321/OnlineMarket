# Project Summary

## Goal

Build an online grocery market with product recommendations and durable ERP integration.

The system must keep the market usable when Recommendation or ERP services are unavailable.

## Applications

| Application | Main responsibility |
|---|---|
| `OnlineMarket.Web` | Identity, customer addresses, catalogue, market stock, cart, checkout, orders, payment simulation, MVC and Admin UI, outbox |
| `Recommendation.Api` | Product and order snapshots, five recommendation algorithms, recommendation queries, recalculation |
| `ErpIntegration.Api` | Durable ERP event intake, ordered steps, retry, attempt history, status APIs |
| `MockErp.Api` | Simulated ERP customer/current account, ERP order, delivery snapshot, stock card and movement, sales accounting voucher, order history, idempotency |

The browser only calls `OnlineMarket.Web`.

## Main Customer Flow

1. Register or sign in.
2. Create or select a delivery address.
3. Browse and filter products.
4. View recommendations.
5. Add products to the cart.
6. Complete a non-sensitive payment simulation.
7. Revalidate price, VAT, product activity, and stock.
8. Create the market order and two outbox events atomically.
9. Show market order success.
10. Complete ERP work asynchronously.
11. Send confirmed order data to the recommendation service.

## Recommendation Types

- Popular products
- Frequently bought together
- Similar products
- Personalized products
- Cart completion

Every recommendation has a score and an understandable reason.

Recommendation failure must not break catalogue, cart, or checkout.

## ERP Scope

The simulated ERP creates and preserves:

- a customer/current-account card with ERP customer code and contact data,
- an ERP order with item, price, VAT, total, payment-method, and delivery-address snapshots,
- an ERP stock card containing SKU, name, unit, net content, quantity, and reorder level,
- one stock movement per sold product,
- one sales accounting voucher per order,
- account-coded accounting lines for customer receivable, domestic sales, and VAT payable,
- a direct customer reference on the accounting voucher,
- customer ERP order history.

The V1 sales voucher uses this project mapping:

- account `120` — Customers/Receivables: debit `GrandTotal`,
- account `600` — Domestic Sales: credit `Subtotal`,
- account `391` — VAT Payable: credit `VatTotal`.

This is a project simulation, not a legal accounting implementation or a real Uyumsoft contract.

If ERP is unavailable, the market order remains valid and the integration retries later.

## Pricing

- Product price is VAT-exclusive.
- Show subtotal, VAT total, and VAT-inclusive grand total.
- Round each line to two decimals with `MidpointRounding.AwayFromZero`.
- Currency is `TRY`.

## Payment

Payment is simulation only.

The non-sensitive `PaymentMethod` value is sent to ERP as an order/accounting snapshot.

Never collect:

- card number,
- CVV,
- expiry date,
- real bank or payment-provider token.

## ERP Parity Boundaries

Deliberately excluded from V1:

- tax number or Turkish identity number,
- open-account balance and maturity tracking,
- supplier/current-account types other than customer,
- draft/approval/cancellation document workflow,
- dispatch note and shipment operations,
- multi-warehouse or location management,
- legal invoice/e-ledger/e-invoice compliance.

## Out of Scope

- Real Uyumsoft API
- Real payment provider
- Delivery tracking
- Mobile application
- Multilingual UI
- Advanced promotions and loyalty
- Real ERP product master synchronisation
- Large-scale machine learning
- RabbitMQ, Kafka, Kubernetes, or user-based collaborative filtering

## Delivery Priority

Protect this order:

1. Identity, address, catalogue, stock
2. Cart and checkout
3. Outbox
4. Recommendation event intake and algorithms
5. Mock ERP
6. ERP worker, retry, and idempotency
7. Web integration and admin monitoring
8. Test, CI, report, and demo
