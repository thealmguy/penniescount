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

The left menu links to **Savings budget**, **Savings items**, **Future look**,
**Savings accounts** and **About**. The homepage retains the headline totals and
shows savings-term totals for Short term, Medium term, Long term, Historic and the
grand total: target, funded allocation, drawdowns, available funds and required
monthly contribution. A second table shows the same columns by purpose category,
including Uncategorised and a grand total. The **Allocated by category** doughnut
shows **Available to spend** by purpose, matching the headline Total allocated;
when no allocations remain available, it shows an empty-state message instead.
Both tables retain exact values if the interactive chart cannot load.
The summary's funded allocation includes drawdowns;
the headline Total allocated instead means funds still available to spend.
Savings items contains the editable allocation tables and removed items; About
explains the calculation and bank-holiday rules.

## Verify

```powershell
dotnet build .\PenniesCount.sln
dotnet test .\PenniesCount.sln
```

Run the full mutation gate, all four configurations below in parallel:

```powershell
.\scripts\mutation.ps1
```

Each gate runs in its own temporary copy of the working tree, so builds never
share `bin` output. Reports are written to `StrykerOutput\parallel-<timestamp>`.
The script prints per-gate and total timings. It fails if any gate fails or any
mutant is Survived, NoCoverage, Timeout or RuntimeError. Optional parameters
tune each gate's test sessions (for example `-LedgerConcurrency 4`).

Each configuration can also be run on its own. The database-free API gate covers
the savings term policy, ledger write rules and violations, dashboard summaries,
future-look calculations, and holiday parsing and caching:

```powershell
Push-Location .\tests\PenniesCount.Api.Tests
try { dotnet stryker } finally { Pop-Location }
```

Run mutation testing for working-day budgeting calculations:

```powershell
Push-Location .\tests\PenniesCount.Core.Tests
try { dotnet stryker --config-file stryker-working-days.json } finally { Pop-Location }
```

Run the database-backed savings ledger gate:

```powershell
Push-Location .\tests\PenniesCount.Api.Tests
try { dotnet stryker --config-file stryker-ledger.json } finally { Pop-Location }
```

Run the details-form deadline validation gate:

```powershell
Push-Location .\tests\PenniesCount.Web.Tests
try { dotnet stryker --config-file stryker-details-validation.json } finally { Pop-Location }
```

Ledger write rules live on `SavingsItem` as domain methods that return a
`LedgerRuleViolation`, and are mutation-tested without a database. The ledger
configuration targets the HTTP and persistence shell around them: the ledger,
payment, reversal and drawdown endpoints, violation-to-response mapping, item
creation and details editing, and the dashboard and future-look queries. It
requires 100% and excludes logging calls/logger category names and
database-generated/default entity initializers from mutation, not financial
logic. Its character spans isolate changed logic in existing files; update
those spans when that code moves.

Database-backed tests lease pooled LocalDB databases named
`PenniesCount_TestPool_<migration-hash>_<slot>`. Each fixture resets its
database when it starts; the ledger, drawdown and future-look classes also
reset before each test.
A per-slot lock file under `%TEMP%\PenniesCount.Tests` keeps concurrent runs
apart. Pooled databases persist between runs to avoid creating a database for
every mutant; adding or removing a migration starts a new pool. If an
existing migration is edited in place, drop the `PenniesCount_TestPool_`
databases for that hash. Migration tests use their own temporary databases. The forecast and
details endpoints' non-financial OpenAPI name and description
strings are outside their mutation spans. The ledger gate retains the
future-look route and database-backed behavior; its term and purpose
aggregation are covered by the database-free gate.
All mutation configurations use local reports
only; no code or reports are uploaded.

The Stryker configurations use the Complete mutation level and
require a 100% mutation score. Strengthen tests for
surviving mutants, then rerun mutation testing and the full test suite before
committing and pushing a green iteration. Local reports are written under
`StrykerOutput` and are not committed.

Use fictional data for testing. Workbook imports, deployment and authentication
are intentionally outside this first pass.

Licensed under [MIT](LICENSE).

## Development approach

Always use small iterative changes, through a test driven development approach where possible, applying BDD-style testing. Always cover non-trivial logic with tests. Once the work is code-complete, and has green tests and builds, apply an adversarial review. Use this review to ensure that acceptance criteria or requirements have been met, code hygiene, structure and maintainability, opportunities to refactor. Act on this feedback. Repeat this a maximum of 3 times. If after this there are outstanding points where the adversarial review outcome is that the acceptance criteria or requirements are not met, defer to the human operator clearly explaining the gap and allow them to give feedback on next steps. Finally, apply mutation testing to help ensure tests are robust and test the right thing. Commit and push after each iteration is green and all mutants are killed.

## Accounts and savings items

Accounts hold a current total and immutable timestamped balance submissions.
Submitting the same balance still records a snapshot. Concurrent changes are
rejected with HTTP 409; refresh and retry rather than overwriting another update.

Create savings items with a name, target, start and deadline, plus an initial
allocation and a required purpose category:

- Emergency Fund
- House & Garden
- Transport
- Holidays & Travel
- Children & Education
- Gifts & Christmas
- Leisure & Technology
- Long-Term Wealth Building
- Other

Existing items stay **Uncategorised** until edited. Uncategorised is not a
selectable category; new items and details edits must select one of the nine
categories. Apply the `SavingsPurposeCategories` migration explicitly with the
database-update command above. It preserves existing data and leaves their
category null; rolling back removes all newly stored purpose categories.

Purpose categories are separate from savings **terms**, which are inferred from
the save-by date using the API's local calendar date, so they change as deadlines
draw nearer:

- **Short term:** today through less than one calendar year away.
- **Medium term:** one through five calendar years away, including both boundaries.
- **Long term:** more than five calendar years away.
- **Historic:** any save-by date before today, whether fully allocated or not.

Calendar-year anniversaries use 28 February for a leap-day date in non-leap years.
The term is not manually selected or persisted. The earlier
`DateDerivedSavingsCategories` migration preserves existing dates, amounts and
IDs. Rolling back that migration recreates the legacy `Type` column as Short for
every item; old manual terms cannot be recovered.

An allocation-only action can replace the
recorded total later; it is stored as a signed **funding adjustment** for the
difference (an unchanged total records nothing but still advances the version).
It does not move money between accounts. Accounts form
one pool; combined allocations may exceed account funds, but each item's
allocation must stay between its total drawdowns and its target. Allocation
means **total funding to date**, including money already spent.

**Edit details** opens a modal to change the name, save-by date and purpose
category. The new deadline must be on/after the original start date; past dates
are allowed. Target amount, start date, funding and ledger history are not
changed. Existing categories are preselected; uncategorised items require a
selection before saving. Updating the deadline recalculates the term and funding
recommendations. Details edits use the same version checks as allocation and
ledger writes; refresh and retry on a conflict. Errors retain the entered values,
Cancel/Escape discards unsaved changes, and dismissal is disabled during save.
Removed items are read-only. Permanent item deletion is not implemented.

Update allocation opens a modal with the current amount. Validation and API
errors stay in the modal without discarding the edit. A successful save closes
the modal and immediately refreshes the budget figures and item rows. Cancel
or Escape discards an unsaved edit; dismissal is disabled while saving.

### Savings item ledger

Each savings item works like a sub-account with an append-only ledger. Entry
types (`type` values) are `0` Opening funding, `1` Allocation payment,
`2` Funding adjustment, `3` Drawdown and `4` Reversal. Total funded allocation
is the sum of effective opening, payment and adjustment entries; drawn down is
the sum of effective drawdowns. Entries are never edited or deleted. A mistake
is undone by a **reversal**: a new entry that references the original, copies
its amount and effective date (so history-based figures stay correct) and
marks it reversed. An entry can be reversed once; reversals cannot themselves be
reversed. Reversals that would take funding below recorded drawdowns or above
the target are rejected.

Creating an item with a nonzero allocation records an opening funding entry
dated today. **Record allocation payment** on the item page records money set
aside on a given date (today or earlier); payments cannot take funding above the
target. Any effective payment dated on an allocation day counts as that day's
payment having been made.

### Drawdowns and removal

Use **Details / draw down** on a budget row to record spending in one amount or
in parts. Each drawdown records a positive GBP amount, spending date and optional
description (up to 200 characters). Dates default to the API's local today;
past dates are allowed, future dates are rejected. Correcting an active drawdown
reverses it and records a replacement with a new ID, a new recorded timestamp
and a link to the drawdown it replaces. Deleting (shown as **Reverse** in the UI)
records a reversal; the original remains visible in the item's history.

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

Apply the `SavingsDrawdowns` migration and then the `SavingsLedger` migration
explicitly using the database update command above. `SavingsLedger` moves
existing drawdowns into the ledger with their IDs, dates, descriptions and
recorded timestamps, and gives each funded item one opening funding entry for
its existing total, dated to the item's start date and flagged as inferred
(`isInferred`) because earlier payment history was never recorded. Rolling back
`SavingsLedger` keeps only drawdowns that are still effective; payments,
adjustments and reversal history are lost. Rolling back `SavingsDrawdowns`
deletes drawdown history and removal state; neither can be recovered by
reapplying a migration.

## Monthly budgeting rules

- Total savings is the sum of current account balances.
- Total allocated includes available-to-spend allocations of non-removed items,
  even future-start items, not gross funding already drawn down.
- Unallocated = savings minus allocated. Positive means spare funds; negative
  means over-allocation.
- Active items spread their unallocated remainder across the first working day
  of each month, on/after today and on/before their inclusive deadline. A working
  day excludes weekends and England-and-Wales public holidays from the official
  GOV.UK feed where coverage is available.
- Today's allocation is counted unless an effective allocation payment for that
  item is recorded for today. Opening funding, funding adjustments and drawdowns
  do not count as an allocation payment.
- A GBP 1,200 target with GBP 300 allocated on 15 January and a 15 April deadline
  has three payments left (1 February, 1 March, 1 April 2027): GBP 300 per month.
- Future-start items require zero until their inclusive start date. Fully
  allocated items require zero.
- If no payment date remains, the full remainder is **needed now** and included
  in the monthly total with a warning, including due/overdue items.
- Historic goals remain in allocation and contribution totals. Their
  underfunded remainder is still needed now; fully allocated past goals require zero.
- Removed items contribute nothing to allocations or recommendations, regardless
  of dates or funding state. Drawdowns do not create a new funding requirement.
- Each recommendation rounds upward to the nearest penny. The overview sums
  those displayed recommendations. Recommendations never change allocations.
- For each month without enough holiday coverage, use its first Monday–Friday.
  The next allocation date, item contributions and overall monthly total remain
  available. Estimates may change when holiday data for those months becomes available.

The **Future look** page projects 24 calendar months, starting with the current
month, with stacked Short, Medium, Long and Historic allocation bars. A second
chart groups the same payments by the nine purpose categories plus Uncategorised;
both charts have identical monthly totals. Each interactive Chart.js tooltip
shows all series' GBP amounts and the monthly total; accessible tables provide
exact figures, even if a chart cannot load. Its read-only forecast includes
future-start goals from their first eligible allocation date, spreads funding
over all remaining eligible dates through each goal's deadline (including
dates beyond the chart), rounds payments up to pennies and caps the final
payment at the amount still required. Goals with no eligible date through
their deadline show their full remainder in the current month as **Needed now**.
This forecast rule can differ from today's monthly recommendation, which
remains zero for goals that have not started. It assumes no interest, inflation,
income, new goals or future recorded payments. Forecast figures are not
automatically recorded in the savings ledger.
Chart.js 4.6.1 is hosted locally under `src/PenniesCount.Web/wwwroot/lib/chart.js`;
its MIT licence is included alongside the distribution. No financial data is
sent to a third-party chart service.

The API uses one host-local calendar date per dashboard, forecast or savings-item
request for all derived categories and calculations. History timestamps use UTC.
Refresh after writes or external changes; the open homepage, Savings items and
Future look also check for a local date change every 30 seconds. Financial
calculations are not persisted or performed in the UI.

The holiday provider validates and caches the England-and-Wales list from
[GOV.UK](https://www.gov.uk/bank-holidays.json). A year is treated as complete
only when the feed has at least one event in both January and December; coverage
is the contiguous sequence of complete years ending at the latest complete year.
No bank holidays are inferred beyond that range. The last-known-good response is
cached at `%LOCALAPPDATA%\PenniesCount\bank-holidays.json` by default; configure
`BankHolidays:CachePath` to override it. The API refreshes about every 24 hours,
retries after failures no more often than every 15 minutes, and reports stale or
unavailable holiday data in the dashboard.

`IContributionSchedule` in Core is the replacement seam for payment-date rules;
`WorkingDaySchedule` uses covered holiday dates, including cached dates, and
falls back to weekdays alone when the holiday calendar cannot determine a month's date.

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
| POST | `/api/savings-items` | `name`, required `category`, `targetAmount`, `allocatedAmount`, `startDate`, `targetDate`; returns item and resource location |
| PUT | `/api/savings-items/{id}/details` | `name`, `targetDate`, required `category`, item `version`; returns updated item without changing funding or ledger history |
| PUT | `/api/savings-items/{id}/allocation` | `allocatedAmount`, `version`; returns updated item |
| GET | `/api/savings-items/{id}/drawdowns` | `asOfDate`, current `item` (including version), and effective (non-reversed) `drawdowns` |
| GET | `/api/savings-items/{id}/drawdowns/{drawdownId}` | One drawdown record, including reversed ones |
| POST | `/api/savings-items/{id}/drawdowns` | `amount`, `spentOn`, optional `description`, item `version`; returns drawdown and resource location |
| PUT | `/api/savings-items/{id}/drawdowns/{drawdownId}` | `amount`, `spentOn`, optional `description`, item `version`; reverses the original and returns the replacement drawdown |
| DELETE | `/api/savings-items/{id}/drawdowns/{drawdownId}` | JSON body with item `version`; records a reversal and returns 204 |
| GET | `/api/savings-items/{id}/ledger` | `asOfDate`, current `item` and every ledger `entries` record |
| GET | `/api/savings-items/{id}/ledger/{entryId}` | One ledger entry |
| POST | `/api/savings-items/{id}/payments` | `amount`, `paidOn`, item `version`; returns the payment entry and resource location |
| POST | `/api/savings-items/{id}/ledger/{entryId}/reversal` | item `version`; returns the reversal entry and resource location |
| PUT | `/api/savings-items/{id}/removal` | item `version`; returns removed item |
| GET | `/api/dashboard` | Overview, grouped items, category and grand totals, `nextAllocationDate` and holiday-calendar status |
| GET | `/api/future-look` | As-of date, holiday status, 24 months with term `categories`, `purposeCategories`, shared `total` and immediate-funding warnings |

Item responses include nullable `category`. Its numeric values are `0` Emergency
Fund, `1` House & Garden, `2` Transport, `3` Holidays & Travel, `4` Children &
Education, `5` Gifts & Christmas, `6` Leisure & Technology, `7` Long-Term Wealth
Building and `8` Other. Null is only valid for existing uncategorised items, not
creation or details writes. The existing `type` remains the derived savings term.
Each forecast month's `purposeCategories` contains all nine categories in this
order, followed by `{ "category": null, "amount": ... }` for Uncategorised,
including zero-valued buckets.

Account `type` values: `0` Cash Account, `1` Cash ISA, `2` Stocks and Shares ISA,
`3` Investment Account. Derived item `type` values: `0` Short, `1` Medium,
`2` Long, `3` Historic (the existing `Past` API enum). Creation requests no longer accept a category
selection; an extra legacy `type` field is ignored, not used for categorisation.
Account/item responses include their ID and an opaque `version` token. Send the
latest token with every update; don't decode it or reuse a stale token.
Item responses also include `drawnDownAmount`, `availableAmount` and `isRemoved`.
Every ledger mutation advances the parent item's version. Reload item/history
after any write to obtain the next token; stale allocation, payment, reversal,
spending and removal requests conflict rather than overwriting concurrent changes.
Each drawdown record contains `id`, `amount`, `spentOn`, `description`,
`recordedAtUtc`, `replacesDrawdownId` and `isReversed`. Each ledger entry contains
`id`, `type`, `amount`, `effectiveDate`, `description`, `recordedAtUtc`,
`isInferred`, `reversesEntryId`, `replacesEntryId` and `isReversed`. Histories are
ordered by effective date, recorded timestamp and ID. Writes to an entry that is
already reversed return 409.

The dashboard returns `asOfDate`, `totalSavings`, `totalAllocated`,
`unallocatedBalance`, nullable `requiredMonthlySavings`, nullable
`nextAllocationDate`, `holidayCalendar` and `groups`. Holiday status has
`source`, nullable `coveredThrough` and `lastSuccessfulRefreshUtc`, `isStale`
and an optional `warning`. Groups always
include Short, Medium, Long and Past in that order. Each group has `type` and `items`;
each row includes the item record, `remainingAmount`, `requiredContribution`,
`remainingPayments`, `paidToday` and `status`. The contribution and payment count
remain nullable in the contract, but missing holiday coverage uses weekday
estimates instead of nulls. Status values: `0` Fully allocated, `1` Not started,
`2` Monthly, `3` Needed now, `4` Unavailable. Items sort by deadline, name and ID within each group; the
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
