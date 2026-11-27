using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class SavingsDrawdownEndpoints
{
    public static void MapSavingsDrawdowns(this WebApplication app)
    {
        var routes = app.MapGroup("/api/savings-items/{id:guid}");
        routes.MapGet("/drawdowns", async Task<IResult> (Guid id, SavingsDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(new SavingsDrawdownHistoryResponse(
                asOf, item.ToResponse(asOf), item.History.Where(x => x.Type == LedgerEntryType.Drawdown && !item.IsReversed(x))
                    .Select(x => x.ToDrawdownResponse(false)).ToArray()));
        });
        routes.MapGet("/drawdowns/{drawdownId:guid}", async Task<IResult> (Guid id, Guid drawdownId,
            SavingsDbContext db, CancellationToken ct) =>
        {
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            var drawdown = item?.FindDrawdown(drawdownId);
            return drawdown is null ? Results.NotFound() : Results.Ok(drawdown.ToDrawdownResponse(item!.IsReversed(drawdown)));
        });
        routes.MapPost("/drawdowns", (Guid id, SaveDrawdownRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
            SaveDrawdownAsync(id, null, request, db, clock, logs.CreateLogger("SavingsItems"), ct));
        routes.MapPut("/drawdowns/{drawdownId:guid}", (Guid id, Guid drawdownId, SaveDrawdownRequest request,
            SavingsDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
            SaveDrawdownAsync(id, drawdownId, request, db, clock, logs.CreateLogger("SavingsItems"), ct));
        routes.MapDelete("/drawdowns/{drawdownId:guid}", async Task<IResult> (Guid id, Guid drawdownId,
            [FromBody] ItemVersionRequest request, SavingsDbContext db, TimeProvider clock, ILoggerFactory logs,
            CancellationToken ct) =>
        {
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var violation = item.ReverseDrawdown(request.Version, drawdownId, clock.GetUtcNow());
            if (violation is not null) return violation.ToResult(logger);
            return await SaveItemAsync(db, item, logger, ct) ?? Results.NoContent();
        });
        routes.MapPut("/removal", async Task<IResult> (Guid id, ItemVersionRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var violation = item.Remove(request.Version);
            if (violation is not null) return violation.ToResult(logger);
            return await SaveItemAsync(db, item, logger, ct) ?? Results.Ok(item.ToResponse(asOf));
        });
    }

    private static async Task<IResult> SaveDrawdownAsync(Guid id, Guid? drawdownId, SaveDrawdownRequest request,
        SavingsDbContext db, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var error = ApiValidation.Check(request, logger);
        if (error is not null) return error;
        var violation = SavingsItem.CheckSpendingDate(request.SpentOn, asOf);
        if (violation is not null) return violation.ToResult(logger);
        var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        violation = item.RecordDrawdown(request.Version, drawdownId, request.Amount, request.SpentOn,
            request.Description, clock.GetUtcNow(), out var drawdown);
        if (violation is not null) return violation.ToResult(logger);
        error = await SaveItemAsync(db, item, logger, ct);
        if (error is not null) return error;
        var response = drawdown!.ToDrawdownResponse(false);
        return drawdownId is null
            ? Results.Created($"/api/savings-items/{id}/drawdowns/{drawdown.Id}", response)
            : Results.Ok(response);
    }

    internal static async Task<IResult?> SaveItemAsync(SavingsDbContext db, SavingsItem item,
        ILogger logger, CancellationToken ct)
    {
        // Ledger writes must also advance/check the parent version, atomically with the history change.
        db.Entry(item).Property(x => x.AllocatedAmount).IsModified = true;
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiValidation.Conflict(logger);
        }
    }
}
