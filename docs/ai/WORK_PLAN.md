# Work Plan Summary

## Team Focus

### Kerem

- Repository and architecture consistency
- Inventory, Ordering, checkout transaction, outbox
- ERP Integration state machine, worker, retry
- Frequently bought together and cart completion
- Critical transaction, concurrency, and integration tests

### Mert

- Identity, customer profile, addresses
- Catalogue, cart, payment simulation
- MVC and Admin UI
- Mock ERP
- Recommendation ingestion, queries, and selected algorithms
- HTTP clients and web integration

### Yudum

- Jira and progress tracking
- API and test documentation
- Deterministic seed and synthetic order profiles
- Recommendation validation
- Manual test evidence
- Report and demo preparation

## Work Order

1. Lock contracts and technical decisions.
2. Prepare repository and shared build setup.
3. Create four database models and initial migrations.
4. Build Identity, address, catalogue, and stock.
5. Build cart, payment simulation, order factories, and checkout.
6. Build outbox event creation and claim/store.
7. Build Mock ERP.
8. Build ERP Integration intake, state machine, adapter, and worker.
9. Build Recommendation intake and algorithms.
10. Connect outbox HTTP delivery and web components.
11. Generate full demo data and validate recommendations.
12. Run cross-service, security, and end-to-end tests.
13. Complete CI, README, report, video, and demo.

## Critical Path

```text
Contracts
-> Solution and DbContexts
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
- another team member reviews the change.
