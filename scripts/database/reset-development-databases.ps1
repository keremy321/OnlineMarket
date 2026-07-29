param(
    [Parameter(Mandatory)]
    [ValidateSet("OnlineMarketDb", "RecommendationDb", "IntegrationDb", "MockErpDb", "All")]
    [string]$Database,

    [Parameter(Mandatory)]
    [switch]$ConfirmReset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $ConfirmReset.IsPresent) {
    throw "Reset confirmation is required. Re-run with both -Database and -ConfirmReset."
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path

function Get-DatabaseTargets {
    @(
        [pscustomobject]@{
            Database = "OnlineMarketDb"
            Context = "OnlineMarketDbContext"
            ConnectionVariable = "ConnectionStrings__OnlineMarketDb"
            Project = Join-Path $repositoryRoot "src\OnlineMarket.Web\OnlineMarket.Web.csproj"
        }
        [pscustomobject]@{
            Database = "RecommendationDb"
            Context = "RecommendationDbContext"
            ConnectionVariable = "ConnectionStrings__RecommendationDb"
            Project = Join-Path $repositoryRoot "src\Recommendation.Api\Recommendation.Api.csproj"
        }
        [pscustomobject]@{
            Database = "IntegrationDb"
            Context = "IntegrationDbContext"
            ConnectionVariable = "ConnectionStrings__IntegrationDb"
            Project = Join-Path $repositoryRoot "src\ErpIntegration.Api\ErpIntegration.Api.csproj"
        }
        [pscustomobject]@{
            Database = "MockErpDb"
            Context = "MockErpDbContext"
            ConnectionVariable = "ConnectionStrings__MockErpDb"
            Project = Join-Path $repositoryRoot "src\MockErp.Api\MockErp.Api.csproj"
        }
    )
}

function Invoke-EfCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = @(& dotnet ef @Arguments 2>&1)
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output
        Text = $output -join [Environment]::NewLine
    }
}

function Restore-EfTool {
    Write-Host "Restoring the repository-local EF Core tool..." -ForegroundColor Cyan
    Push-Location $repositoryRoot
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            throw "The repository-local EF Core tool could not be restored."
        }
    }
    finally {
        Pop-Location
    }
}

function Test-LocalTargetConnection {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target
    )

    $connectionString = [Environment]::GetEnvironmentVariable($Target.ConnectionVariable, "Process")
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        return [pscustomobject]@{
            IsValid = $false
            Detail = "Required environment variable '$($Target.ConnectionVariable)' is missing."
        }
    }

    try {
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($connectionString)
    }
    catch {
        return [pscustomobject]@{
            IsValid = $false
            Detail = "Environment variable '$($Target.ConnectionVariable)' is not a valid connection string."
        }
    }

    $databaseName = if ($builder.ContainsKey("Database")) {
        "$($builder["Database"])"
    }
    elseif ($builder.ContainsKey("Initial Catalog")) {
        "$($builder["Initial Catalog"])"
    }
    else {
        ""
    }

    if ($databaseName -ne $Target.Database) {
        return [pscustomobject]@{
            IsValid = $false
            Detail = "The configured database name does not exactly match '$($Target.Database)'."
        }
    }

    $dataSource = if ($builder.ContainsKey("Server")) {
        "$($builder["Server"])"
    }
    elseif ($builder.ContainsKey("Data Source")) {
        "$($builder["Data Source"])"
    }
    else {
        ""
    }

    if ($dataSource -notmatch "^(?i:tcp:)?(?i:127\.0\.0\.1|localhost)(,\d+)?$") {
        return [pscustomobject]@{
            IsValid = $false
            Detail = "The configured server must be local (127.0.0.1 or localhost) for this development reset."
        }
    }

    [pscustomobject]@{ IsValid = $true; Detail = "Local target is explicitly configured." }
}

function Get-DatabaseReadiness {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target
    )

    if (-not (Test-Path -LiteralPath $Target.Project -PathType Leaf)) {
        return [pscustomobject]@{ State = "ProjectMissing"; Detail = "Owning project is missing." }
    }

    # EF Core's dbcontext list command has no --context option. It is isolated by
    # explicit project and startup-project, and the exact expected context is matched.
    $contextResult = Invoke-EfCommand -Arguments @(
        "dbcontext", "list",
        "--project", $Target.Project,
        "--startup-project", $Target.Project,
        "--no-color"
    )

    if ($contextResult.ExitCode -ne 0) {
        if ($contextResult.Text -match "No DbContext was found|doesn't reference Microsoft\.EntityFrameworkCore\.Design|Unable to create a 'DbContext'") {
            return [pscustomobject]@{ State = "ContextMissing"; Detail = "The expected DbContext is not available to EF tooling." }
        }

        return [pscustomobject]@{ State = "Failed"; Detail = "DbContext discovery failed: $($contextResult.Text.Trim())" }
    }

    $contextPattern = "(?m)(^|\.)$([regex]::Escape($Target.Context))\s*$"
    if ($contextResult.Text -notmatch $contextPattern) {
        return [pscustomobject]@{ State = "ContextMissing"; Detail = "Expected context '$($Target.Context)' was not discovered." }
    }

    $migrationResult = Invoke-EfCommand -Arguments @(
        "migrations", "list",
        "--project", $Target.Project,
        "--startup-project", $Target.Project,
        "--context", $Target.Context,
        "--no-connect",
        "--no-color"
    )

    if ($migrationResult.ExitCode -ne 0) {
        return [pscustomobject]@{ State = "Failed"; Detail = "Migration discovery failed: $($migrationResult.Text.Trim())" }
    }

    if ($migrationResult.Text -match "No migrations were found" -or
        $migrationResult.Text -notmatch "(?m)^\s*\d{8,14}_[^\s(]+") {
        return [pscustomobject]@{ State = "MigrationMissing"; Detail = "No existing EF Core migration was discovered." }
    }

    [pscustomobject]@{ State = "Ready"; Detail = "DbContext and existing migrations were discovered." }
}

$allTargets = @(Get-DatabaseTargets)
$selectedTargets = if ($Database -eq "All") {
    $allTargets
}
else {
    @($allTargets | Where-Object { $_.Database -eq $Database })
}

if ($selectedTargets.Count -eq 0) {
    throw "No approved project database matched '$Database'."
}

Write-Host "Selected development database targets:" -ForegroundColor Yellow
$selectedTargets.Database | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
Write-Host "Docker volumes and SQL Server system databases are outside this operation." -ForegroundColor Yellow

$previousEnvironment = [Environment]::GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Process")
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development", "Process")

$summary = @()
$resetFailure = $false

try {
    Restore-EfTool

    foreach ($target in $selectedTargets) {
        Write-Host "Inspecting $($target.Database)..." -ForegroundColor Cyan
        $readiness = Get-DatabaseReadiness -Target $target

        if ($readiness.State -ne "Ready") {
            Write-Host "Skipping $($target.Database): $($readiness.State) - $($readiness.Detail)" -ForegroundColor Yellow
            $summary += [pscustomobject]@{
                Database = $target.Database
                Readiness = $readiness.State
                Drop = "Skipped"
                Reapply = "Skipped"
            }
            continue
        }

        $connectionCheck = Test-LocalTargetConnection -Target $target
        if (-not $connectionCheck.IsValid) {
            $resetFailure = $true
            Write-Host "Refusing to reset $($target.Database): $($connectionCheck.Detail)" -ForegroundColor Red
            $summary += [pscustomobject]@{
                Database = $target.Database
                Readiness = $readiness.State
                Drop = "Failed safety check"
                Reapply = "Not attempted"
            }
            continue
        }

        Write-Host "Operation 1/2 - dropping only $($target.Database)..." -ForegroundColor Red
        $dropResult = Invoke-EfCommand -Arguments @(
            "database", "drop",
            "--force",
            "--project", $target.Project,
            "--startup-project", $target.Project,
            "--context", $target.Context,
            "--no-color",
            "--",
            "--environment", "Development"
        )

        if ($dropResult.ExitCode -ne 0) {
            $resetFailure = $true
            Write-Host $dropResult.Text -ForegroundColor Red
            $summary += [pscustomobject]@{
                Database = $target.Database
                Readiness = $readiness.State
                Drop = "Failed"
                Reapply = "Not attempted"
            }
            continue
        }

        Write-Host "Operation 2/2 - reapplying existing migrations to $($target.Database)..." -ForegroundColor Green
        $updateResult = Invoke-EfCommand -Arguments @(
            "database", "update",
            "--project", $target.Project,
            "--startup-project", $target.Project,
            "--context", $target.Context,
            "--no-color",
            "--",
            "--environment", "Development"
        )

        if ($updateResult.ExitCode -ne 0) {
            $resetFailure = $true
            Write-Host $updateResult.Text -ForegroundColor Red
            Write-Host "The original $($target.Database) database is absent because its drop succeeded. Migration reapplication failed and may have left a partial replacement; do not treat it as usable." -ForegroundColor Red
            $reapplyStatus = "Failed; original absent"
        }
        else {
            $reapplyStatus = "Succeeded"
        }

        $summary += [pscustomobject]@{
            Database = $target.Database
            Readiness = $readiness.State
            Drop = "Succeeded"
            Reapply = $reapplyStatus
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", $previousEnvironment, "Process")
}

Write-Host "`nReset summary:" -ForegroundColor Cyan
$summary | Format-Table -AutoSize

if ($resetFailure) {
    Write-Error "One or more selected Ready databases failed during reset."
    exit 1
}
