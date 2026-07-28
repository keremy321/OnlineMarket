# PowerShell script to verify database connectivity and schema
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
$rootDir = Resolve-Path "$scriptDir/../.."

Write-Host "Verifying OnlineMarket.Web database connectivity..." -ForegroundColor Green
dotnet ef dbcontext info --project "$rootDir/src/OnlineMarket.Web/OnlineMarket.Web.csproj"

Write-Host "Verification complete!" -ForegroundColor Green
