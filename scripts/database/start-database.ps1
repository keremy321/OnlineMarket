# PowerShell script to start SQL Server container
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
$rootDir = Resolve-Path "$scriptDir/../.."
$composeFile = "$rootDir/deploy/docker-compose.database.yml"

Write-Host "Starting SQL Server 2022 development container..." -ForegroundColor Green
docker compose -f $composeFile up -d

Write-Host "Waiting for SQL Server to become healthy..." -ForegroundColor Yellow
$healthy = $false
for ($i = 1; $i -le 30; $i++) {
    $status = docker inspect --format='{{json .State.Health.Status}}' online-market-sqlserver 2>$null
    if ($status -eq '"healthy"') {
        $healthy = $true
        break
    }
    Start-Sleep -Seconds 2
}

if ($healthy) {
    Write-Host "SQL Server container is running and healthy!" -ForegroundColor Green
} else {
    Write-Host "Warning: SQL Server container health check did not complete in time." -ForegroundColor Red
}
