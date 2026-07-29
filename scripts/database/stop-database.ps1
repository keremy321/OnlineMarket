# PowerShell script to stop SQL Server container
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
$rootDir = Resolve-Path "$scriptDir/../.."
$composeFile = "$rootDir/deploy/docker-compose.database.yml"

Write-Host "Stopping SQL Server 2022 development container..." -ForegroundColor Yellow
docker compose -f $composeFile down
Write-Host "SQL Server container stopped." -ForegroundColor Green
