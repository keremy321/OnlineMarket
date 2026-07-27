# Online Market System

Online market, recommendation engine and ERP integration simulation developed with .NET 10 and ASP.NET Core.

## Applications

- OnlineMarket.Web
- Recommendation.Api
- ErpIntegration.Api
- MockErp.Api

## Requirements

- .NET SDK 10.0.302
- Visual Studio 2026
- SQL Server 2022 Express or Developer

## Build

```powershell
dotnet restore OnlineMarket.slnx
dotnet build OnlineMarket.slnx -c Release
dotnet test OnlineMarket.slnx -c Release