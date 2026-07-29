# Online Market — Coding Agent Instructions

This file is the main instruction source for Codex, Antigravity, Claude, and other coding agents.

## Repository

- Solution: `OnlineMarket.slnx`
- IDE: Visual Studio 2026
- Runtime: .NET 10
- Web UI: ASP.NET Core MVC
- APIs: ASP.NET Core Web API with controllers
- ORM: Entity Framework Core 10
- Database: SQL Server 2022
- Tests: xUnit

Applications:

- `src/OnlineMarket.Web`
- `src/Recommendation.Api`
- `src/ErpIntegration.Api`
- `src/MockErp.Api`

Each application has one matching test project under `tests/`.

## How to Load Context

Always read:

1. `AGENTS.md`
2. `docs/ai/README.md`

Then read only the compact document selected for the task. Do not load all project documents unless an exact detail is missing.

## Hard Rules

1. Production projects must not reference each other.
2. Applications communicate through HTTP and versioned JSON contracts.
3. Each application accesses only its own database.
4. No cross-database queries, joins, foreign keys, views, or procedures.
5. The browser communicates only with `OnlineMarket.Web`.
6. Controllers must not access `DbContext` directly.
7. Keep business workflows in application services.
8. Keep EF Core, SQL, HTTP clients, and workers in infrastructure.
9. Do not expose entities as API responses or MVC ViewModels.
10. Do not create Generic Repository, Generic Service, Shared Contracts, MediatR, message brokers, or extra frameworks without an approved decision.
11. Keep `Program.cs` focused on dependency registration and middleware.
12. Never commit secrets, connection strings, API keys, or real customer data.
13. Follow SOLID principles across all component and class designs.
14. Apply OWASP security standards (e.g. input validation, injection protection, authorization).
15. Implement comprehensive exception handling and logging for failure scenarios.

## Checkout Rules

A successful checkout must save all of these in one `OnlineMarketDb` transaction:

- Order
- Order address snapshot
- Order items with price and VAT snapshots
- Successful payment simulation
- Atomic stock decreases
- Stock movements
- Cart status `Converted`
- `OrderConfirmedForRecommendationV1` outbox message
- `OrderReadyForErpV1` outbox message

External HTTP calls are forbidden inside the checkout transaction.

ERP transfer status is not stored as the main status of the market Order. It is queried from `ErpIntegration.Api`.

## Recommendation Rules

V1 contains exactly five recommendation types:

1. Popular
2. Frequently bought together
3. Similar
4. Personalized
5. Cart completion

Use confirmed order snapshots only.

- Missing product snapshot: reject the order event with `Recommendation.ProductSnapshotMissing` as retryable.
- Stale product event: do not overwrite a newer snapshot; compare `SourceUpdatedAtUtc`.
- Exclude duplicate, inactive, out-of-stock, source, and cart products.
- Use popular products as fallback when personal history is insufficient.
- `OnlineMarket.Web` performs the final current stock and activity check.

## ERP Rules

ERP steps run in this order:

1. Ensure customer
2. Create order
3. Create stock movement
4. Create accounting entry

The worker must:

- resume from the first incomplete step,
- never rerun a succeeded step,
- keep the same idempotency key during retries,
- persist every attempt,
- distinguish transient and permanent failures.

Mock ERP is a project simulation, not a real Uyumsoft contract.


## Before Editing

State:

- the Jira/task ID,
- affected application and module,
- files to create or change,
- schema or contract impact,
- tests and commands that will verify the result.

Show the plan before editing unless the user explicitly requests immediate implementation.

## After Editing

Run the relevant focused tests, then:

```powershell
dotnet build .\OnlineMarket.slnx -c Release
dotnet test .\OnlineMarket.slnx -c Release
```

After package changes:

```powershell
dotnet package list --project .\OnlineMarket.slnx --include-transitive --vulnerable
```

Do not claim completion if build or relevant tests fail.
