Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$composeFile = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot "deploy\docker-compose.database.yml")).Path
$environmentFile = Join-Path $repositoryRoot "deploy\.env"
$containerName = "online-market-sqlserver"

if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
    throw "Missing local environment file '$environmentFile'. Copy deploy/.env.example to deploy/.env and replace the non-production password placeholder."
}

Write-Host "Starting the pinned SQL Server 2022 development container..." -ForegroundColor Green
& docker compose --env-file $environmentFile -f $composeFile up -d
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose could not start the SQL Server development container."
}

Write-Host "Waiting up to 60 seconds for the container health check..." -ForegroundColor Yellow
$isHealthy = $false
for ($attempt = 1; $attempt -le 30; $attempt++) {
    $healthStatus = & docker inspect --format "{{.State.Health.Status}}" $containerName 2>$null
    if ($LASTEXITCODE -eq 0 -and $healthStatus -eq "healthy") {
        $isHealthy = $true
        break
    }

    Start-Sleep -Seconds 2
}

if (-not $isHealthy) {
    throw "The SQL Server container did not become healthy within 60 seconds. Inspect it with 'docker inspect $containerName'."
}

Write-Host "SQL Server is running and healthy." -ForegroundColor Green
