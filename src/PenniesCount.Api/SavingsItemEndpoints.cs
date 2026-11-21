using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class SavingsItemEndpoints
{
    public static void MapSavingsItems(this WebApplication app)
    {
        var routes = app.MapGroup("/api/savings-items");
        routes.MapGet("/", async (SavingsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var items = await db.SavingsItems.AsNoTracking().ToListAsync(ct);
            return new SavingsItemsResponse(asOf, items.Select(x => x.ToResponse(asOf))
                .OrderBy(x => x.Type).ThenBy(x => x.TargetDate).ThenBy(x => x.Name).ThenBy(x => x.Id).ToArray());
        });
        routes.MapGet("/{id:guid}", async Task<IResult> (Guid id, SavingsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var item = await db.SavingsItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item.ToResponse(asOf));
        });
        routes.MapPost("/", async Task<IResult> (CreateSavingsItemRequest request, SavingsDbContext db,
            TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var error = ApiValidation.Check(request, logs.CreateLogger("SavingsItems"));
            if (error is not null) return error;
            var item = new SavingsItem
            {
                Name = request.Name.Trim(), TargetAmount = request.TargetAmount,
                AllocatedAmount = request.AllocatedAmount, StartDate = request.StartDate, TargetDate = request.TargetDate
            };
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
            var item = await db.SavingsItems.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            if (Convert.ToBase64String(item.Version) != request.Version)
                return ApiValidation.Conflict(logger);
            if (request.AllocatedAmount > item.TargetAmount)
            {
                logger.LogWarning("Item allocation validation failed.");
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.AllocatedAmount)] = ["Allocated amount cannot exceed the target."]
                });
            }
            item.AllocatedAmount = request.AllocatedAmount;
            db.Entry(item).Property(x => x.AllocatedAmount).IsModified = true;
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(item.ToResponse(asOf));
            }
            catch (DbUpdateConcurrencyException)
            {
                return ApiValidation.Conflict(logger);
            }
        });
    }
}
