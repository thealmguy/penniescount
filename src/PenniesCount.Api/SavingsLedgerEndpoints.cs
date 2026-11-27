using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class SavingsLedgerEndpoints
{
    public static void MapSavingsLedger(this WebApplication app)
    {
        var routes = app.MapGroup("/api/savings-items/{id:guid}");
        routes.MapGet("/ledger", async Task<IResult> (Guid id, SavingsDbContext db, TimeProvider clock,
            CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(new SavingsLedgerResponse(asOf,
                item.ToResponse(asOf), item.History.Select(x => x.ToResponse(item.IsReversed(x))).ToArray()));
        });
        routes.MapGet("/ledger/{entryId:guid}", async Task<IResult> (Guid id, Guid entryId, SavingsDbContext db,
            CancellationToken ct) =>
        {
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            var entry = item?.Entries.SingleOrDefault(x => x.Id == entryId);
            return entry is null ? Results.NotFound() : Results.Ok(entry.ToResponse(item!.IsReversed(entry)));
        });
        routes.MapPost("/payments", async Task<IResult> (Guid id, RecordPaymentRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var violation = SavingsItem.CheckPaymentDate(request.PaidOn, asOf);
            if (violation is not null) return violation.ToResult(logger);
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            violation = item.RecordPayment(request.Version, request.Amount, request.PaidOn, clock.GetUtcNow(),
                out var payment);
            if (violation is not null) return violation.ToResult(logger);
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct)
                ?? Results.Created($"/api/savings-items/{id}/ledger/{payment!.Id}", payment.ToResponse(false));
        });
        routes.MapPost("/ledger/{entryId:guid}/reversal", async Task<IResult> (Guid id, Guid entryId,
            ItemVersionRequest request, SavingsDbContext db, TimeProvider clock, ILoggerFactory logs,
            CancellationToken ct) =>
        {
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var violation = item.ReverseEntry(request.Version, entryId, clock.GetUtcNow(), out var reversal);
            if (violation is not null) return violation.ToResult(logger);
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct)
                ?? Results.Created($"/api/savings-items/{id}/ledger/{reversal!.Id}", reversal.ToResponse(false));
        });
    }
}
