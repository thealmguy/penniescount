# PenniesCount

A local, single-user savings budget experiment. Blazor calls a separate HTTP
API; financial logic and SQL Server persistence live behind that API.

## Prerequisites

- Windows, .NET 10 SDK, and SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).
- No authentication is implemented. Run on loopback only, not a shared network.
- Never commit real financial data, spreadsheets, database files or secrets.

## Run locally

From the repository root, in two terminals:

```powershell
dotnet run --project .\src\PenniesCount.Api
dotnet run --project .\src\PenniesCount.Web
```

Open `http://localhost:5101`. The API listens at `http://localhost:5100`.
This development setup uses HTTP on loopback, not a production hosting setup.

## Verify

```powershell
dotnet build .\PenniesCount.sln
dotnet test .\PenniesCount.sln
```

Use fictional data for testing. Workbook imports, charts, deployment and
authentication are intentionally outside this first pass.

Licensed under [MIT](LICENSE).
