# Database Development Guide

This guide explains how to set up, migrate, verify, and reset local SQL Server databases for development.

## Preferred Environment

- **SQL Server**: SQL Server 2022 Docker Container (`deploy/docker-compose.database.yml`)
- **Connection Strings**: Configured via User Secrets or environment variables.

---

## 1. Starting SQL Server Container

```powershell
.\scripts\database\start-database.ps1
```

Or manually via Docker Compose:

```powershell
docker compose -f deploy/docker-compose.database.yml up -d
```

---

## 2. Configuring User Secrets

Set local connection strings using User Secrets for `OnlineMarket.Web`:

```powershell
dotnet user-secrets set "ConnectionStrings:OnlineMarketDb" "Server=127.0.0.1,1433;Database=OnlineMarketDb;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True;" --project .\src\OnlineMarket.Web\OnlineMarket.Web.csproj
```

---

## 3. Creating & Applying Migrations

To add a new migration:

```powershell
dotnet ef migrations add <MigrationName> --project .\src\OnlineMarket.Web\OnlineMarket.Web.csproj --output-dir Infrastructure/Persistence/Migrations
```

To apply all migrations to local SQL Server:

```powershell
.\scripts\database\migrate-all.ps1
```

---

## 4. Resetting & Verifying Databases

To verify connectivity and current DbContext status:

```powershell
.\scripts\database\verify-databases.ps1
```

To drop and recreate local development databases:

```powershell
.\scripts\database\reset-development-databases.ps1
```

---

## 5. Stopping SQL Server

```powershell
.\scripts\database\stop-database.ps1
```

---

## Troubleshooting Connectivity

1. **Docker container failing health check**: Verify `MSSQL_SA_PASSWORD` meets SQL Server complexity requirements (min 8 chars, uppercase, lowercase, digit, special char).
2. **Connection Refused**: Ensure container is running via `docker ps` and port `1433` is not bound by a local SQL Server service instance.
3. **SSL / Certificate errors**: Append `TrustServerCertificate=True;` to connection string in local development.
