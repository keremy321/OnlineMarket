# Work Plan Summary

## Team Focus

### Kerem

- Repository and architecture consistency
- Inventory, Ordering, checkout transaction, outbox
- ERP Integration state machine, worker, retry
- ERP event and accounting-contract consistency
- Frequently bought together and cart completion
- Critical transaction, concurrency, and integration tests

### Mert

- Identity, customer profile, addresses
- Catalogue, cart, payment simulation
- MVC and Admin UI
- Mock ERP customer, order, stock, accounting, and order-history features
- Recommendation ingestion, queries, and selected algorithms
- HTTP clients and web integration

### Yudum

- Jira and progress tracking
- API and test documentation
- ERP field-to-model parity checklist
- Deterministic seed and synthetic order profiles
- Recommendation validation
- Manual test evidence
- Report and demo preparation

## Work Order

1. Lock contracts and technical decisions.
2. Prepare repository and shared build setup.
3. Create four database models and initial migrations.
4. Complete ERP data parity:
   - payment method in the ERP event and snapshots,
   - ERP order delivery-address snapshot,
   - ERP stock unit/net-content/reorder-level fields,
   - accounting voucher header and account-coded lines.
5. Build Identity, address, catalogue, and stock.
6. Build cart, payment simulation, order factories, and checkout.
7. Build outbox event creation and claim/store.
8. Build Mock ERP.
9. Build ERP Integration intake, state machine, adapter, and worker.
10. Build Recommendation intake and algorithms.
11. Connect outbox HTTP delivery and web components.
12. Generate full demo data and validate recommendations.
13. Run cross-service, security, and end-to-end tests.
14. Complete CI, README, report, video, and demo.

## ERP Data-Parity Checklist

Before Mock ERP and ERP Integration are marked ready:

- `OrderReadyForErpV1` carries `PaymentMethod`.
- Integration persists payment method with the durable order snapshot.
- Mock ERP persists the order delivery address as an immutable snapshot.
- Mock ERP stock cards persist `UnitType`, `NetContent`, and `ReorderLevel`.
- Accounting has a header and account-coded lines.
- Accounting has a direct ERP customer reference.
- The V1 voucher creates accounts `120`, `600`, and `391`.
- Debit and credit totals are equal.
- Tax/identity number, open balance, supplier type, dispatch, and
  multi-warehouse data remain explicitly out of scope.

## Critical Path

```text
Contracts
-> Solution and DbContexts
-> ERP data parity
-> Catalogue and stock
-> Order and event factories
-> Checkout transaction
-> Outbox
-> Mock ERP
-> ERP Integration worker
-> Recommendation event intake
-> HTTP delivery
-> End-to-end test
-> Demo
```

## Jira Rules

- Epic: major stage
- Story/task: deliverable feature
- Checklist: small implementation steps
- Do not create one Jira issue for every checkbox.
- A task cannot move to Ready before dependencies are complete.
- One active development story per person is preferred.

Priority:

- P0: blocks the project
- P1: required for the main demo
- P2: useful but deferrable
- P3: optional value

## Definition of Done

A task is done when:

- acceptance criteria are met,
- architecture and module boundaries are preserved,
- relevant tests pass,
- build passes,
- no secret or personal data is added,
- schema or contract documents are updated when affected,
- ERP field-to-model parity remains valid when ERP data changes,
- another team member reviews the change.
