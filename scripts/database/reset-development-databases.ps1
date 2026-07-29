# PowerShell script to drop and recreate development databases via EF Core
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
$rootDir = Resolve-Path "$scriptDir/../.."

Write-Host "Resetting OnlineMarketDb..." -ForegroundColor Yellow
dotnet ef database drop --force --project "$rootDir/src/OnlineMarket.Web/OnlineMarket.Web.csproj"
dotnet ef database update --project "$rootDir/src/OnlineMarket.Web/OnlineMarket.Web.csproj"

Write-Host "OnlineMarketDb reset complete!" -ForegroundColor Green
