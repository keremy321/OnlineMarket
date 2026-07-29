Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$containerName = "online-market-sqlserver"
$expectedDatabaseNames = @(
    "OnlineMarketDb",
    "RecommendationDb",
    "IntegrationDb",
    "MockErpDb"
)

function Get-DatabaseTargets {
    @(
        [pscustomobject]@{
            Database = "OnlineMarketDb"
            Context = "OnlineMarketDbContext"
            Project = Join-Path $repositoryRoot "src\OnlineMarket.Web\OnlineMarket.Web.csproj"
        }
        [pscustomobject]@{
            Database = "RecommendationDb"
            Context = "RecommendationDbContext"
            Project = Join-Path $repositoryRoot "src\Recommendation.Api\Recommendation.Api.csproj"
        }
        [pscustomobject]@{
            Database = "IntegrationDb"
            Context = "IntegrationDbContext"
            Project = Join-Path $repositoryRoot "src\ErpIntegration.Api\ErpIntegration.Api.csproj"
        }
        [pscustomobject]@{
            Database = "MockErpDb"
            Context = "MockErpDbContext"
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

Restore-EfTool
$readinessSummary = @()
$verificationFailure = $false

foreach ($target in Get-DatabaseTargets) {
    $readiness = Get-DatabaseReadiness -Target $target
    if ($readiness.State -eq "Failed") {
        $verificationFailure = $true
    }

    $readinessSummary += [pscustomobject]@{
        Database = $target.Database
        Context = $target.Context
        State = $readiness.State
        Detail = $readiness.Detail
    }
}

Write-Host "`nEF Core readiness summary:" -ForegroundColor Cyan
$readinessSummary | Format-Table -AutoSize -Wrap

$databaseRuntimeSummary = @()
$dockerStatusOutput = @(& docker info --format "{{.ServerVersion}}" 2>$null)
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker runtime verification is pending because the Docker engine is unavailable." -ForegroundColor Yellow
    foreach ($databaseName in $expectedDatabaseNames) {
        $databaseRuntimeSummary += [pscustomobject]@{ Database = $databaseName; Runtime = "Pending" }
    }
}
else {
    $containerStatus = @(& docker inspect --format "{{.State.Running}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}" $containerName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $containerStatus.Count -eq 0) {
        Write-Host "Docker is available, but the SQL Server container does not exist. Runtime verification is pending." -ForegroundColor Yellow
        foreach ($databaseName in $expectedDatabaseNames) {
            $databaseRuntimeSummary += [pscustomobject]@{ Database = $databaseName; Runtime = "Pending" }
        }
    }
    elseif ($containerStatus[0] -ne "true|healthy") {
        Write-Host "The SQL Server container is not running and healthy. Runtime verification is pending." -ForegroundColor Yellow
        foreach ($databaseName in $expectedDatabaseNames) {
            $databaseRuntimeSummary += [pscustomobject]@{ Database = $databaseName; Runtime = "Pending" }
        }
    }
    else {
        # sqlcmd runs inside the container and reads the password only from the
        # container environment. Base64 transport preserves nested Bash quoting
        # on Windows without embedding or echoing the credential.
        $queryCommand = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT [name] FROM sys.databases WHERE [name] IN (''OnlineMarketDb'',''RecommendationDb'',''IntegrationDb'',''MockErpDb'') ORDER BY [name];"'
        $queryPayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($queryCommand))
        $queryRunner = "echo $queryPayload|base64 -d|/bin/bash"
        $queryOutput = @(& docker exec $containerName /bin/bash -c $queryRunner 2>$null)
        if ($LASTEXITCODE -ne 0) {
            $verificationFailure = $true
            Write-Host "SQL Server connectivity verification failed inside the container. Credential details were suppressed." -ForegroundColor Red
            foreach ($databaseName in $expectedDatabaseNames) {
                $databaseRuntimeSummary += [pscustomobject]@{ Database = $databaseName; Runtime = "QueryFailed" }
            }
        }
        else {
            $foundDatabaseNames = @(
                $queryOutput |
                    ForEach-Object { "$_".Trim() } |
                    Where-Object { $expectedDatabaseNames -contains $_ }
            )

            foreach ($databaseName in $expectedDatabaseNames) {
                $runtimeState = if ($foundDatabaseNames -contains $databaseName) { "Present" } else { "Missing" }
                $databaseRuntimeSummary += [pscustomobject]@{ Database = $databaseName; Runtime = $runtimeState }
            }
        }
    }
}

Write-Host "`nSQL Server project database summary:" -ForegroundColor Cyan
$databaseRuntimeSummary | Format-Table -AutoSize

if ($verificationFailure) {
    Write-Error "Database verification found one or more failures."
    exit 1
}
