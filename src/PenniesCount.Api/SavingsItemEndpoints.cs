using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class SavingsItemEndpoints
{
    public static void MapSavingsItems(this WebApplication app)
    {
        var routes = app.MapGroup("/api/savings-items");
        routes.MapGet("/", async (bool? removed, SavingsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var items = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .Where(x => x.IsRemoved == (removed ?? false)).ToListAsync(ct);
            return new SavingsItemsResponse(asOf, items.Select(x => x.ToResponse(asOf))
                .OrderBy(x => x.Type).ThenBy(x => x.TargetDate).ThenBy(x => x.Name).ThenBy(x => x.Id).ToArray());
        });
        routes.MapGet("/{id:guid}", async Task<IResult> (Guid id, SavingsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item.ToResponse(asOf));
        });
        routes.MapPost("/", async Task<IResult> (CreateSavingsItemRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var error = ApiValidation.Check(request, logs.CreateLogger("SavingsItems"));
            if (error is not null) return error;
            var item = SavingsItem.Open(request, asOf, clock.GetUtcNow());
            db.SavingsItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/savings-items/{item.Id}", item.ToResponse(asOf));
        });
        routes.MapPut("/{id:guid}/allocation", async Task<IResult> (Guid id, UpdateAllocationRequest request,
            SavingsDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var violation = item.SetTotalFunding(request.Version, request.AllocatedAmount, asOf, clock.GetUtcNow());
            if (violation is not null) return violation.ToResult(logger);
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct) ?? Results.Ok(item.ToResponse(asOf));
        });
        routes.MapPut("/{id:guid}/details", async Task<IResult> (Guid id, UpdateSavingsItemDetailsRequest request,
            SavingsDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var logger = logs.CreateLogger("SavingsItems");
            var error = ApiValidation.Check(request, logger);
            if (error is not null) return error;
            var item = await db.SavingsItems.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var violation = item.UpdateDetails(request.Version, request.Name, request.TargetDate, request.Category);
            if (violation is not null) return violation.ToResult(logger);
            return await SavingsDrawdownEndpoints.SaveItemAsync(db, item, logger, ct) ?? Results.Ok(item.ToResponse(asOf));
        }).WithName("UpdateSavingsItemDetails").WithSummary("Edit savings item details")
            .WithDescription("Update name, save-by date and required purpose category without changing funding or ledger history.")
            .Produces<SavingsItemResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
