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
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Drawdowns)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(new SavingsDrawdownHistoryResponse(
                asOf, item.ToResponse(asOf), item.Drawdowns.OrderBy(x => x.SpentOn)
                    .ThenBy(x => x.RecordedAtUtc).ThenBy(x => x.Id).Select(x => x.ToResponse()).ToArray()));
        });
        routes.MapGet("/drawdowns/{drawdownId:guid}", async Task<IResult> (Guid id, Guid drawdownId,
            SavingsDbContext db, CancellationToken ct) =>
        {
            var drawdown = await db.Drawdowns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SavingsItemId == id && x.Id == drawdownId, ct);
            return drawdown is null ? Results.NotFound() : Results.Ok(drawdown.ToResponse());
        });
        routes.MapPost("/drawdowns", (Guid id, SaveDrawdownRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
            SaveDrawdownAsync(id, null, request, db, clock, logs.CreateLogger("SavingsItems"), ct));
        routes.MapPut("/drawdowns/{drawdownId:guid}", (Guid id, Guid drawdownId, SaveDrawdownRequest request,
            SavingsDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
            SaveDrawdownAsync(id, drawdownId, request, db, clock, logs.CreateLogger("SavingsItems"), ct));
        routes.MapDelete("/drawdowns/{drawdownId:guid}", async Task<IResult> (Guid id, Guid drawdownId,
            [FromBody] ItemVersionRequest request, SavingsDbContext db, ILoggerFactory logs, CancellationToken ct) =>
        {
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Drawdowns).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            error = CheckWritable(item, request.Version, logger);
            if (error is not null) return error;
            var drawdown = item.Drawdowns.SingleOrDefault(x => x.Id == drawdownId);
            if (drawdown is null) return Results.NotFound();
            db.Drawdowns.Remove(drawdown);
            return await SaveItemAsync(db, item, logger, ct) ?? Results.NoContent();
        });
        routes.MapPut("/removal", async Task<IResult> (Guid id, ItemVersionRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Drawdowns).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            error = CheckWritable(item, request.Version, logger);
            if (error is not null) return error;
            item.IsRemoved = true;
            return await SaveItemAsync(db, item, logger, ct) ?? Results.Ok(item.ToResponse(asOf));
        });
    }

    private static async Task<IResult> SaveDrawdownAsync(Guid id, Guid? drawdownId, SaveDrawdownRequest request,
        SavingsDbContext db, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var error = ApiValidation.Check(request, logger);
        if (error is not null) return error;
        if (request.SpentOn == DateOnly.MinValue || request.SpentOn > asOf)
        {
            logger.LogWarning("Drawdown date validation failed.");
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.SpentOn)] = ["Spending date is required and cannot be in the future."]
            });
        }
        var item = await db.SavingsItems.Include(x => x.Drawdowns).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        error = CheckWritable(item, request.Version, logger);
        if (error is not null) return error;
        var existing = item.Drawdowns.SingleOrDefault(x => x.Id == drawdownId);
        if (drawdownId.HasValue && existing is null) return Results.NotFound();
        var total = item.Drawdowns.Sum(x => x.Amount) - (existing?.Amount ?? 0m) + request.Amount;
        if (total > item.AllocatedAmount)
        {
            logger.LogWarning("Drawdown amount validation failed.");
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Amount)] = ["Drawdowns cannot exceed total funded allocation."]
            });
        }
        var drawdown = existing ?? new SavingsItemDrawdown
        {
            SavingsItemId = id, RecordedAtUtc = clock.GetUtcNow()
        };
        drawdown.Amount = request.Amount;
        drawdown.SpentOn = request.SpentOn;
        drawdown.Description = request.Description?.Trim();
        if (existing is null) db.Drawdowns.Add(drawdown);
        error = await SaveItemAsync(db, item, logger, ct);
        if (error is not null) return error;
        return existing is null
            ? Results.Created($"/api/savings-items/{id}/drawdowns/{drawdown.Id}", drawdown.ToResponse())
            : Results.Ok(drawdown.ToResponse());
    }

    private static IResult? CheckWritable(SavingsItem item, string version, ILogger logger)
    {
        if (Convert.ToBase64String(item.Version) != version) return ApiValidation.Conflict(logger);
        return item.IsRemoved ? Removed(logger) : null;
    }

    internal static IResult Removed(ILogger logger)
    {
        logger.LogWarning("A write to a removed savings item was rejected.");
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Removed items are read-only.");
    }

    private static async Task<IResult?> SaveItemAsync(SavingsDbContext db, SavingsItem item,
        ILogger logger, CancellationToken ct)
    {
        // Child writes must also advance/check the parent version, atomically with the history change.
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
