# PenniesCount

A local, single-user savings budget experiment. Blazor calls a separate HTTP
API; financial logic and SQL Server persistence live behind that API.

## Prerequisites

- Windows, .NET 10 SDK, and SQL Server LocalDB (`(localdb)\MSSQLLocalDB`).
- No authentication is implemented. Run on loopback only, not a shared network.
- Never commit real financial data, spreadsheets, database files or secrets.

## Run locally

From the repository root, first restore tooling and create/update the
application's database explicitly:

```powershell
dotnet tool restore
dotnet ef database update --project .\src\PenniesCount.Api
```

Migrations are never applied automatically at startup. The app uses a database
named `PenniesCount` with Windows integrated authentication. Do not point tests
at your real database: they create and delete uniquely named
`PenniesCount_Test_<random>` databases on LocalDB.

Then run these commands in two separate terminals:

```powershell
dotnet run --project .\src\PenniesCount.Api
dotnet run --project .\src\PenniesCount.Web
```

Open `http://localhost:5101`. The API listens at `http://localhost:5100`.
This development setup uses HTTP on loopback, not a production hosting setup.
No financial demo data is seeded.

## Verify

```powershell
dotnet build .\PenniesCount.sln
dotnet test .\PenniesCount.sln
```

Run mutation testing for the date-derived savings category policy:

```powershell
Push-Location .\tests\PenniesCount.Api.Tests
try { dotnet stryker } finally { Pop-Location }
```

Run the drawdown lifecycle gate in the same test directory:

```powershell
Push-Location .\tests\PenniesCount.Api.Tests
try { dotnet stryker --config-file stryker-drawdowns.json } finally { Pop-Location }
```

The drawdown configuration targets the new lifecycle and changed allocation,
removal and dashboard calculations. It requires 100% and excludes logging
calls/logger category names and database-generated/default entity initializers
from mutation, not financial logic. Its character spans isolate changed logic
in existing files; update those spans when that code moves. Both configurations use local reports
only; no code or reports are uploaded.

The committed Stryker configuration uses the Complete mutation level and
requires a 100% mutation score for `SavingsTermClassifier`. Strengthen tests for
surviving mutants, then rerun mutation testing and the full test suite before
committing and pushing a green iteration. Local reports are written under
`StrykerOutput` and are not committed.

Use fictional data for testing. Workbook imports, charts, deployment and
authentication are intentionally outside this first pass.

Licensed under [MIT](LICENSE).

## Development approach

Always use small iterative changes, through a test driven development approach where possible, applying BDD-style testing. Always cover non-trivial logic with tests. Apply mutation testing to help ensure tests are robust and test the right thing. Commit and push after each iteration is green and all mutants are killed.

## Accounts and savings items

Accounts hold a current total and immutable timestamped balance submissions.
Submitting the same balance still records a snapshot. Concurrent changes are
rejected with HTTP 409; refresh and retry rather than overwriting another update.

Create savings items with a name, target, start and deadline, plus an initial
allocation. Categories are inferred from the save-by date using the API's local
calendar date, so they change as deadlines draw nearer:

- **Short term:** today through less than one calendar year away.
- **Medium term:** one through five calendar years away, including both boundaries.
- **Long term:** more than five calendar years away.
- **Past savings goals:** any save-by date before today, whether fully allocated or not.

Calendar-year anniversaries use 28 February for a leap-day date in non-leap years.
There is no manual category selection or persisted category. Apply the latest
migration explicitly using the database-update command above; existing dates,
amounts and IDs are preserved. Rolling back that migration recreates the legacy
category column as Short for every item; old manual categories cannot be recovered.

An allocation-only action can replace the
recorded amount later. It does not move money between accounts. Accounts form
one pool; combined allocations may exceed account funds, but each item's
allocation must stay between its total drawdowns and its target. Allocation
means **total funding to date**, including money already spent. General item
editing and permanent item deletion are not implemented.

Update allocation opens a modal with the current amount. Validation and API
errors stay in the modal without discarding the edit. A successful save closes
the modal and immediately refreshes the budget figures and item rows. Cancel
or Escape discards an unsaved edit; dismissal is disabled while saving.

### Drawdowns and removal

Use **Details / draw down** on a budget row to record spending in one amount or
in parts. Each drawdown records a positive GBP amount, spending date and optional
description (up to 200 characters). Dates default to the API's local today;
past dates are allowed, future dates are rejected. Active-item history can be
corrected or deleted with confirmation. The recorded UTC timestamp is preserved
when correcting a record; this is correctable history, not an immutable audit ledger.

Drawdowns do not change the original target or total funded allocation.
**Available to spend** is funded allocation minus drawdowns; **Funding remaining**
is target minus funded allocation. A GBP 10,000 target funded with GBP 10,000
and a GBP 3,000 flights drawdown remains Fully allocated, with GBP 7,000 available
and zero additional funding needed. Total spending cannot exceed funding.
Later allocation updates cannot reduce total funding below recorded spending.

Drawdowns never update account balances, versions or balance snapshots. Update
accounts manually when money actually moves. Dashboard Total allocated decreases
as spending is recorded, so apparent spare funds may be temporary until account
balances are updated.

Full depletion does not automatically remove an item. **Mark removed** is a
separate confirmed action, allowed even if funding is incomplete or money remains.
It excludes the item from every item-derived budget sum but does not manufacture
spending or update accounts. **Removed items** retains read-only figures and
history; there is no restore action. Removed items reject all further writes.

Apply the new additive `SavingsDrawdowns` migration explicitly using the database
update command above. Existing items and amounts are preserved and start active
with no drawdowns. Rolling back this migration deletes drawdown history and
removal state; it cannot be recovered by reapplying the migration.

## Monthly budgeting rules

- Total savings is the sum of current account balances.
- Total allocated includes available-to-spend allocations of non-removed items,
  even future-start items, not gross funding already drawn down.
- Unallocated = savings minus allocated. Positive means spare funds; negative
  means over-allocation.
- Active items spread their unallocated remainder across 1st-of-month dates
  strictly after today and on/before their deadline. Today's allocation is
  assumed already recorded, even on the 1st.
- A GBP 1,200 target with GBP 300 allocated on 15 January and a 15 April deadline
  has three payments left (1 February, 1 March, 1 April): GBP 300 per month.
- Future-start items require zero until their inclusive start date. Fully
  allocated items require zero.
- If no payment date remains, the full remainder is **needed now** and included
  in the monthly total with a warning, including due/overdue items.
- Past savings goals remain in allocation and contribution totals. Their
  underfunded remainder is still needed now; fully allocated past goals require zero.
- Removed items contribute nothing to allocations or recommendations, regardless
  of dates or funding state. Drawdowns do not create a new funding requirement.
- Each recommendation rounds upward to the nearest penny. The overview sums
  those displayed recommendations. Recommendations never change allocations.

The API uses one host-local calendar date per dashboard or savings-item request
for all derived categories and calculations. History
timestamps use UTC. Refresh after writes or external changes; the open homepage
also checks for a local date change every 30 seconds. Financial calculations
are not persisted or performed in the UI.

`IContributionSchedule` in Core is the replacement seam for payment-date rules;
`FirstOfMonthSchedule` is the only supported policy in this pass.

## API contract

All write requests use `Content-Type: application/json`. Amounts are JSON
numbers with at most two decimal places, from zero to
`9999999999999999.99`; item targets must be greater than zero. Names are
required and limited to 100 characters. Goal dates use `YYYY-MM-DD`; target
dates must be on/after start dates. Past dates are supported.

| Method | Path | Request / response |
| --- | --- | --- |
| GET | `/api/health` | `{ "status": "Ready" }` (process health, not database readiness) |
| GET | `/api/accounts` | Array of account records |
| GET | `/api/accounts/{id}` | One account record |
| POST | `/api/accounts` | `providerName`, `accountName`, `type`, `totalFunds`; returns account and resource location |
| PUT | `/api/accounts/{id}/balance` | `totalFunds`, `version`; returns updated account |
| GET | `/api/accounts/{id}/balance-history` | Array of `id`, `totalFunds`, `recordedAtUtc`, ordered by timestamp then ID |
| GET | `/api/savings-items` | `asOfDate` and active `items` array; `?removed=true` lists only removed items |
| GET | `/api/savings-items/{id}` | One savings item |
| POST | `/api/savings-items` | `name`, `targetAmount`, `allocatedAmount`, `startDate`, `targetDate`; returns item and resource location |
| PUT | `/api/savings-items/{id}/allocation` | `allocatedAmount`, `version`; returns updated item |
| GET | `/api/savings-items/{id}/drawdowns` | `asOfDate`, current `item` (including version), and `drawdowns` |
| GET | `/api/savings-items/{id}/drawdowns/{drawdownId}` | One drawdown record |
| POST | `/api/savings-items/{id}/drawdowns` | `amount`, `spentOn`, optional `description`, item `version`; returns drawdown and resource location |
| PUT | `/api/savings-items/{id}/drawdowns/{drawdownId}` | `amount`, `spentOn`, optional `description`, item `version`; returns corrected drawdown |
| DELETE | `/api/savings-items/{id}/drawdowns/{drawdownId}` | JSON body with item `version`; returns 204 |
| PUT | `/api/savings-items/{id}/removal` | item `version`; returns removed item |
| GET | `/api/dashboard` | Overview and calculated grouped items |

Account `type` values: `0` Cash Account, `1` Cash ISA, `2` Stocks and Shares ISA,
`3` Investment Account. Derived item `type` values: `0` Short, `1` Medium,
`2` Long, `3` Past savings goals. Creation requests no longer accept a category
selection; an extra legacy `type` field is ignored, not used for categorisation.
Account/item responses include their ID and an opaque `version` token. Send the
latest token with every update; don't decode it or reuse a stale token.
Item responses also include `drawnDownAmount`, `availableAmount` and `isRemoved`.
Every drawdown mutation advances the parent item's version. Reload item/history
after create, correction or deletion to obtain the next token; stale allocation,
spending and removal requests conflict rather than overwriting concurrent changes.
Each history record contains `id`, `amount`, `spentOn`, `description` and
`recordedAtUtc`, ordered by spending date, recorded timestamp and ID.

The dashboard returns `asOfDate`, `totalSavings`, `totalAllocated`,
`unallocatedBalance`, `requiredMonthlySavings` and `groups`. Groups always
include Short, Medium, Long and Past in that order. Each group has `type` and `items`;
each row includes the item record, `remainingAmount`, `requiredContribution`,
`remainingPayments` and `status`. Status values: `0` Fully allocated, `1` Not
started, `2` Monthly, `3` Needed now. Items sort by deadline, name and ID within each group; the
savings-item list sorts first by derived category, then deadline, name and ID.

Creation returns 201; reads/updates return 200; drawdown deletion returns 204. Validation failures return 400
Problem Details with field errors, missing IDs return 404, stale versions
return 409, removed-item writes return 409, and non-JSON writes return 415. Unexpected failures are not replaced
with empty records or zero totals.
Fictional request examples are in [SavingsDrawdowns.http](src/PenniesCount.Api/SavingsDrawdowns.http).

The contract types live in [Contracts](src/PenniesCount.Contracts). The
calculation policy lives in [Core](src/PenniesCount.Core). The Blazor project
has no Core or database reference.

## Configuration and verification notes

For a private database override, set `ConnectionStrings__Savings` in the API
terminal (and when applying migrations); `ApiBaseUrl` can override the Web
client's API address. Environment variables are not committed. Keep both
servers on this computer's loopback interface. There is no permissive CORS
policy, and there are no authenticated/public-network usage guarantees.

Tests use xUnit Given/When/Then scenarios, bUnit for UI behavior, and genuine
SQL Server LocalDB databases for persistence. Core tests have no external
dependencies; date-rollover tests advance a fake clock rather than sleeping.
On the development machine, the current Core scenarios completed in about
0.06 seconds, UI scenarios in about 0.5 seconds, and API scenarios in about
1 second (reported test execution times, not total build/startup time).

Each implementation iteration is built and fully tested before commit/push.
Browser checks use fictional data and disposable databases, not workbook data.
