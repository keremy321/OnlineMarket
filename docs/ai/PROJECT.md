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
| `MockErp.Api` | Simulated ERP customer, order, stock movement, accounting, order history, idempotency |

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

The simulated ERP creates:

- customer account,
- ERP order,
- ERP stock movement,
- balanced accounting entry,
- customer ERP order history.

If ERP is unavailable, the market order remains valid and the integration retries later.

## Pricing

- Product price is VAT-exclusive.
- Show subtotal, VAT total, and VAT-inclusive grand total.
- Round each line to two decimals with `MidpointRounding.AwayFromZero`.
- Currency is `TRY`.

## Payment

Payment is simulation only.

Never collect:

- card number,
- CVV,
- expiry date,
- real bank or payment-provider token.

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
