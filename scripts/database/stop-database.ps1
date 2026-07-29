Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$composeFile = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot "deploy\docker-compose.database.yml")).Path
$environmentFile = Join-Path $repositoryRoot "deploy\.env"

if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
    throw "Missing local environment file '$environmentFile'. The Compose environment is required even when stopping the container."
}

Write-Host "Stopping the SQL Server development container while preserving its volume..." -ForegroundColor Yellow
& docker compose --env-file $environmentFile -f $composeFile down
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose could not stop the SQL Server development container."
}

Write-Host "SQL Server container stopped. The sqlserver_data volume was preserved." -ForegroundColor Green
