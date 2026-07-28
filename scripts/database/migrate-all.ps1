# PowerShell script to apply EF Core migrations
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
$rootDir = Resolve-Path "$scriptDir/../.."

Write-Host "Applying EF Core migrations for OnlineMarket.Web..." -ForegroundColor Green
dotnet ef database update --project "$rootDir/src/OnlineMarket.Web/OnlineMarket.Web.csproj"

Write-Host "All migrations applied successfully!" -ForegroundColor Green
