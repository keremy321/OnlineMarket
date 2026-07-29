Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$composeFile = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot "deploy\docker-compose.database.yml")).Path
$environmentFile = Join-Path $repositoryRoot "deploy\.env"
$containerName = "online-market-sqlserver"
$volumeInitServiceName = "sqlserver-volume-init"
$databaseServiceName = "sqlserver"

if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
    throw "Missing local environment file '$environmentFile'. Copy deploy/.env.example to deploy/.env and replace the non-production password placeholder."
}

Write-Host "Preparing SQL Server volume ownership for the image-default non-root user..." -ForegroundColor Cyan
& docker compose --env-file $environmentFile -f $composeFile up --no-deps --force-recreate $volumeInitServiceName
if ($LASTEXITCODE -ne 0) {
    throw "The SQL Server volume initialization service failed."
}

$volumeInitContainerId = & docker compose --env-file $environmentFile -f $composeFile ps --all --quiet $volumeInitServiceName
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($volumeInitContainerId)) {
    throw "The SQL Server volume initialization container could not be inspected."
}

$volumeInitState = & docker inspect --format "{{.State.Status}}|{{.State.ExitCode}}" $volumeInitContainerId 2>$null
if ($LASTEXITCODE -ne 0 -or $volumeInitState -ne "exited|0") {
    throw "The SQL Server volume initialization container did not complete successfully."
}

Write-Host "SQL Server volume ownership preparation completed successfully." -ForegroundColor Green
Write-Host "Starting the pinned SQL Server 2022 development container..." -ForegroundColor Green
& docker compose --env-file $environmentFile -f $composeFile up -d --no-deps $databaseServiceName
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
