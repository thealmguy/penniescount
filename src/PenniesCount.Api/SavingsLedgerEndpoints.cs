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
            if (request.PaidOn == DateOnly.MinValue || request.PaidOn > asOf)
                return Invalid(logger, nameof(request.PaidOn), "Payment date is required and cannot be in the future.");
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            error = SavingsDrawdownEndpoints.CheckWritable(item, request.Version, logger);
            if (error is not null) return error;
            if (item.FundedAmount + request.Amount > item.TargetAmount)
                return Invalid(logger, nameof(request.Amount), "Payments cannot take total funding above the target.");
            var payment = item.Record(LedgerEntryType.AllocationPayment, request.Amount, request.PaidOn,
                null, clock.GetUtcNow());
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct)
                ?? Results.Created($"/api/savings-items/{id}/ledger/{payment.Id}", payment.ToResponse(false));
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
            error = SavingsDrawdownEndpoints.CheckWritable(item, request.Version, logger);
            if (error is not null) return error;
            var original = item.Entries.SingleOrDefault(x => x.Id == entryId);
            if (original is null) return Results.NotFound();
            if (original.Type == LedgerEntryType.Reversal)
                return Invalid(logger, "EntryId", "Reversal entries cannot be reversed.");
            if (item.IsReversed(original)) return SavingsDrawdownEndpoints.AlreadyReversed(logger);
            var funded = item.FundedAmount - original.FundingEffect;
            if (funded < item.DrawnDownAmount - original.DrawdownEffect || funded > item.TargetAmount)
                return Invalid(logger, "EntryId",
                    "Reversing this entry would take total funding below recorded drawdowns or above the target.");
            var reversal = item.Record(LedgerEntryType.Reversal, original.Amount, original.EffectiveDate, null,
                clock.GetUtcNow(), reverses: original.Id);
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct)
                ?? Results.Created($"/api/savings-items/{id}/ledger/{reversal.Id}", reversal.ToResponse(false));
        });
    }

    private static IResult Invalid(ILogger logger, string field, string message)
    {
        logger.LogWarning("Ledger validation failed.");
        return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    }
}
