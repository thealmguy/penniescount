# PenniesCount

A local, single-user savings budget experiment. Blazor calls a separate HTTP
API; financial logic and SQL Server persistence live behind that API.

## Prerequisites

- Windows, .NET 10 SDK, and SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).
- No authentication is implemented. Run on loopback only, not a shared network.
- Never commit real financial data, spreadsheets, database files or secrets.

## Run locally

From the repository root, in two terminals:

First create/update the application's database explicitly:

```powershell
dotnet tool restore
dotnet ef database update --project .\src\PenniesCount.Api
```

Migrations are never applied automatically at startup. The app uses a database
named `PenniesCount` with Windows integrated authentication. Do not point tests
at your real database: they create and delete uniquely named
`PenniesCount_Test_<random>` databases on LocalDB.

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

## Accounts and savings items

Accounts hold a current total and immutable timestamped balance submissions.
Submitting the same balance still records a snapshot. Concurrent changes are
rejected with HTTP 409; refresh and retry rather than overwriting another update.

Create Short, Medium or Long term savings items with a name, target, start and
deadline, plus an initial allocation. An allocation-only action can replace the
recorded amount later. It does not move money between accounts. Accounts form
one pool; combined allocations may exceed account funds, but each item's
allocation must stay between zero and its target. General editing and deletion
are not implemented.
