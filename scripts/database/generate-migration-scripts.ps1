param(
    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$outputDirectory = Join-Path $repositoryRoot "scripts\database\generated"

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
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $outputDirectory
}

$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$summary = @()
$generationFailure = $false

foreach ($target in Get-DatabaseTargets) {
    Write-Host "Inspecting $($target.Database)..." -ForegroundColor Cyan
    $readiness = Get-DatabaseReadiness -Target $target

    if ($readiness.State -ne "Ready") {
        Write-Host "Skipping $($target.Database): $($readiness.State) - $($readiness.Detail)" -ForegroundColor Yellow
        $summary += [pscustomobject]@{
            Database = $target.Database
            Readiness = $readiness.State
            Generation = "Skipped"
            Output = ""
        }
        continue
    }

    $outputFile = Join-Path $outputDirectory "$($target.Database)_$timestamp.sql"
    if ((Test-Path -LiteralPath $outputFile -PathType Leaf) -and -not $Overwrite) {
        $generationFailure = $true
        Write-Host "Refusing to overwrite '$outputFile'. Re-run with -Overwrite to replace this timestamped file." -ForegroundColor Red
        $summary += [pscustomobject]@{
            Database = $target.Database
            Readiness = $readiness.State
            Generation = "Failed"
            Output = $outputFile
        }
        continue
    }

    Write-Host "Generating an idempotent SQL script for $($target.Database)..." -ForegroundColor Green
    $scriptResult = Invoke-EfCommand -Arguments @(
        "migrations", "script",
        "--idempotent",
        "--output", $outputFile,
        "--project", $target.Project,
        "--startup-project", $target.Project,
        "--context", $target.Context,
        "--no-color",
        "--",
        "--environment", "Development"
    )

    if ($scriptResult.ExitCode -eq 0) {
        $generationStatus = "Succeeded"
    }
    else {
        $generationFailure = $true
        $generationStatus = "Failed"
        Write-Host $scriptResult.Text -ForegroundColor Red
    }

    $summary += [pscustomobject]@{
        Database = $target.Database
        Readiness = $readiness.State
        Generation = $generationStatus
        Output = $outputFile
    }
}

Write-Host "`nSQL generation summary:" -ForegroundColor Cyan
$summary | Format-Table -AutoSize

if ($generationFailure) {
    Write-Error "One or more Ready database contexts failed SQL script generation."
    exit 1
}
