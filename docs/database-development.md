# Local Database Development

This runbook covers the four independent SQL Server databases used by the
Online Market applications. The preferred local runtime is the repository's
SQL Server 2022 Docker Compose service.

| Database | Owning application | EF Core context |
|---|---|---|
| `OnlineMarketDb` | `OnlineMarket.Web` | `OnlineMarketDbContext` |
| `RecommendationDb` | `Recommendation.Api` | `RecommendationDbContext` |
| `IntegrationDb` | `ErpIntegration.Api` | `IntegrationDbContext` |
| `MockErpDb` | `MockErp.Api` | `MockErpDbContext` |

The databases remain isolated. The tooling does not create cross-database
objects, queries, joins, foreign keys, views, or procedures.

## Prerequisites

- .NET 10 SDK
- Docker Desktop with Linux containers
- PowerShell 7 or Windows PowerShell 5.1

Restore the repository-local EF Core tool from any directory inside the
repository:

```powershell
dotnet tool restore
```

The manifest pins `dotnet-ef` 10.0.0. A global installation is not required.

## SQL Server container configuration

The Compose service uses this immutable image reference:

```text
mcr.microsoft.com/mssql/server:2022-CU22-GDR1-ubuntu-22.04@sha256:bf438d7104f861f5e1e1ba14b063d60a5bd883964b3c9b3b3c0064536177baa5
```

Create the ignored local environment file and replace the visible placeholder
with a strong password used only on your machine:

```powershell
Copy-Item .\deploy\.env.example .\deploy\.env
```

`deploy/.env` must define both `ACCEPT_EULA` and `MSSQL_SA_PASSWORD`. Compose
fails with a clear error when either value is absent. Never commit this file or
paste its password into logs, issues, documentation, or source-controlled
configuration.

Start the container:

```powershell
.\scripts\database\start-database.ps1
```

The script uses the absolute Compose and environment-file paths resolved from
its own location, waits for the container health check, and returns a nonzero
exit code if SQL Server does not become healthy.

Equivalent manual Compose commands must include the environment file:

```powershell
docker compose --env-file .\deploy\.env -f .\deploy\docker-compose.database.yml up -d
docker compose --env-file .\deploy\.env -f .\deploy\docker-compose.database.yml ps
```

Stop the container without deleting the named data volume:

```powershell
.\scripts\database\stop-database.ps1
```

Do not add `--volumes` to the stop operation unless deletion of all local SQL
Server data has been separately reviewed and approved.

## Application connection strings

Committed appsettings files must not contain SQL credentials. For current local
development, configure these exact process environment variables:

- `ConnectionStrings__OnlineMarketDb`
- `ConnectionStrings__RecommendationDb`
- `ConnectionStrings__IntegrationDb`
- `ConnectionStrings__MockErpDb`

Example PowerShell values use a deliberately non-functional placeholder. Replace
`<LOCAL_ONLY_PASSWORD>` in your local shell only:

```powershell
$env:ConnectionStrings__OnlineMarketDb = 'Server=127.0.0.1,1433;Database=OnlineMarketDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;'
$env:ConnectionStrings__RecommendationDb = 'Server=127.0.0.1,1433;Database=RecommendationDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;'
$env:ConnectionStrings__IntegrationDb = 'Server=127.0.0.1,1433;Database=IntegrationDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;'
$env:ConnectionStrings__MockErpDb = 'Server=127.0.0.1,1433;Database=MockErpDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;'
```

### User Secrets commands

The projects do not currently contain `UserSecretsId` metadata. Adding that
project metadata is outside this environment-only phase. After a separately
approved phase initializes User Secrets for each application, use these exact
commands with a local-only password:

```powershell
dotnet user-secrets set "ConnectionStrings:OnlineMarketDb" "Server=127.0.0.1,1433;Database=OnlineMarketDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;" --project .\src\OnlineMarket.Web\OnlineMarket.Web.csproj
dotnet user-secrets set "ConnectionStrings:RecommendationDb" "Server=127.0.0.1,1433;Database=RecommendationDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;" --project .\src\Recommendation.Api\Recommendation.Api.csproj
dotnet user-secrets set "ConnectionStrings:IntegrationDb" "Server=127.0.0.1,1433;Database=IntegrationDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;" --project .\src\ErpIntegration.Api\ErpIntegration.Api.csproj
dotnet user-secrets set "ConnectionStrings:MockErpDb" "Server=127.0.0.1,1433;Database=MockErpDb;User Id=sa;Password=<LOCAL_ONLY_PASSWORD>;Encrypt=True;TrustServerCertificate=True;" --project .\src\MockErp.Api\MockErp.Api.csproj
```

Do not run `dotnet user-secrets init` as an unreviewed side effect of database
tooling.

## Readiness classification

Database scripts restore the local tool and ask EF Core to discover the owning
project's context and migrations. A folder or source filename alone is not
treated as proof of readiness.

Each target receives exactly one state:

- `ProjectMissing`: the owning project file does not exist.
- `ContextMissing`: the expected context is unavailable to EF tooling.
- `MigrationMissing`: the context exists but no migration is discoverable.
- `Ready`: the expected context and at least one existing migration are
  discoverable.
- `Failed`: EF discovery encountered an unexpected failure.

`dbcontext list` is constrained with explicit `--project` and
`--startup-project`; EF Core does not support `--context` on that discovery
command. Every context-specific migrations, update, drop, and script command
supplies `--project`, `--startup-project`, and `--context`.

## Apply existing migrations

Set the four application connection strings, start SQL Server, then run:

```powershell
.\scripts\database\migrate-all.ps1
```

The script applies existing migrations only. It never creates a migration,
calls `EnsureCreated`, or modifies a model snapshot. Incomplete contexts are
reported and skipped. A failed update for a `Ready` context returns a nonzero
exit code, and the final table reports every database. Before updating a
`Ready` context, the script requires its exact connection-string environment
variable and verifies that it names the expected database on `127.0.0.1` or
`localhost`. This prevents the existing Web LocalDB fallback or a remote server
from being selected implicitly.

The current branch has an implemented context and migration only for
`OnlineMarketDb`. Until their database foundation phases land,
`RecommendationDb`, `IntegrationDb`, and `MockErpDb` are expected to be
classified as `ContextMissing` and skipped.

## Generate idempotent SQL

Generate timestamped, idempotent SQL for every `Ready` context:

```powershell
.\scripts\database\generate-migration-scripts.ps1
```

Files are written to the ignored absolute directory
`scripts/database/generated/`. A timestamp collision is not overwritten unless
`-Overwrite` is explicitly supplied. Incomplete contexts are reported and
skipped. Generated SQL must be reviewed before it is used outside local
development.

## Verify contexts and database presence

Run:

```powershell
.\scripts\database\verify-databases.ps1
```

The script reports EF readiness for all four owners. If the container is
healthy, it runs `/opt/mssql-tools18/bin/sqlcmd` inside the container with
certificate trust enabled; host `sqlcmd` is not required. The SQL query checks
only the four project database names listed at the top of this document and
does not treat system databases as project databases. The SA password is read
from the container environment and is not printed.

An unavailable Docker engine or stopped/missing container is reported as
`Pending`, not as proof that a project database exists.

## Reset selected development databases

Reset is destructive and requires both a validated target and the confirmation
switch:

```powershell
.\scripts\database\reset-development-databases.ps1 -Database OnlineMarketDb -ConfirmReset
.\scripts\database\reset-development-databases.ps1 -Database RecommendationDb -ConfirmReset
.\scripts\database\reset-development-databases.ps1 -Database IntegrationDb -ConfirmReset
.\scripts\database\reset-development-databases.ps1 -Database MockErpDb -ConfirmReset
.\scripts\database\reset-development-databases.ps1 -Database All -ConfirmReset
```

The script sets `ASPNETCORE_ENVIRONMENT=Development` for its process, prints the
selected targets, and performs each drop and migration reapplication as
separate operations. It never discovers targets with wildcards, touches an
unselected or system database, or removes Docker volumes. Incomplete targets
are skipped. A `Ready` target is rejected before the drop unless its exact
connection-string environment variable names that database on `127.0.0.1` or
`localhost`. If reapplication fails after a successful drop, the script returns
nonzero and reports that the original database is absent and any partial
replacement is unusable.

## Test environments

Database integration tests should prefer an isolated SQL Server Testcontainers
instance when their owning application phase adds those tests. Testcontainers
must use non-production credentials and disposable storage.

LocalDB may be used as a Windows-only fallback for focused development where
Docker is unavailable, but it does not replace validation against the pinned
SQL Server 2022 container. Never point reset or migration scripts at a shared,
staging, or production database.

## Troubleshooting

- Required Compose variable error: copy `deploy/.env.example` to the ignored
  `deploy/.env` and replace its visible placeholder locally.
- Password policy error: use at least eight characters with uppercase,
  lowercase, numeric, and special characters.
- Port 1433 conflict: stop the conflicting local service or change the local
  port mapping and all four local connection strings together.
- TLS error: retain `Encrypt=True;TrustServerCertificate=True` for this local
  development container only.
- EF state `ContextMissing`: implement and register the owning context in its
  approved application database phase; do not create an empty migration as a
  workaround.
- EF state `MigrationMissing`: create the initial migration only in the owning
  application's approved schema phase; these shared scripts intentionally do
  not generate migrations.

## Known deferred risks

- `OnlineMarket.Web` currently applies migrations and seeds data during
  application startup. Removing that behavior requires a separately approved
  application change and is not handled by these environment scripts.
- The repository audit found a high-severity advisory affecting the current
  `Microsoft.OpenApi` 2.0.0 dependency graph. Package alignment belongs in a
  separate package-security task; this phase does not change package
  references.
- The pinned image's internal sqlcmd path, container health, connectivity,
  database creation, and migration application require Docker runtime
  verification. Static Compose validation cannot prove those runtime
  behaviors.
