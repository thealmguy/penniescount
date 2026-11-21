using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class AccountEndpoints
{
    public static void MapAccounts(this WebApplication app)
    {
        var routes = app.MapGroup("/api/accounts");
        routes.MapGet("/", async (SavingsDbContext db, CancellationToken ct) =>
            (await db.Accounts.AsNoTracking().OrderBy(x => x.ProviderName).ThenBy(x => x.AccountName)
                .ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToResponse()));
        routes.MapGet("/{id:guid}", async Task<IResult> (Guid id, SavingsDbContext db, CancellationToken ct) =>
        {
            var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return account is null ? Results.NotFound() : Results.Ok(account.ToResponse());
        });
        routes.MapPost("/", async Task<IResult> (CreateAccountRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var error = ApiValidation.Check(request, logs.CreateLogger("Accounts"));
            if (error is not null) return error;
            var account = new Account
            {
                ProviderName = request.ProviderName.Trim(), AccountName = request.AccountName.Trim(),
                Type = request.Type, TotalFunds = request.TotalFunds,
                History = [new() { TotalFunds = request.TotalFunds, RecordedAtUtc = clock.GetUtcNow() }]
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/accounts/{account.Id}", account.ToResponse());
        });
        routes.MapPut("/{id:guid}/balance", async Task<IResult> (Guid id, UpdateBalanceRequest request,
            SavingsDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var logger = logs.CreateLogger("Accounts");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (account is null) return Results.NotFound();
            if (Convert.ToBase64String(account.Version) != request.Version)
                return ApiValidation.Conflict(logger);
            account.TotalFunds = request.TotalFunds;
            // Equal balances are also submissions: force an UPDATE to advance the concurrency token.
            db.Entry(account).Property(x => x.TotalFunds).IsModified = true;
            db.BalanceSnapshots.Add(new()
            {
                AccountId = id, TotalFunds = request.TotalFunds, RecordedAtUtc = clock.GetUtcNow()
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(account.ToResponse());
            }
            catch (DbUpdateConcurrencyException)
            {
                return ApiValidation.Conflict(logger);
            }
        });
        routes.MapGet("/{id:guid}/balance-history", async Task<IResult> (Guid id, SavingsDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Accounts.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var history = await db.BalanceSnapshots.AsNoTracking().Where(x => x.AccountId == id)
                .OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.Id)
                .Select(x => new BalanceSnapshotResponse(x.Id, x.TotalFunds, x.RecordedAtUtc)).ToListAsync(ct);
            return Results.Ok(history);
        });
    }
}
