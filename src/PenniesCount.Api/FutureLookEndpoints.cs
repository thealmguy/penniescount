using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;

namespace PenniesCount.Api;

internal static class FutureLookEndpoints
{
    public static void MapFutureLook(this WebApplication app) =>
        app.MapGet("/api/future-look", async (SavingsDbContext db, TimeProvider clock,
            IBankHolidayProvider holidayProvider, CancellationToken ct) =>
        {
            var snapshot = await BudgetSnapshot.LoadAsync(db, clock, holidayProvider, ct);
            return TypedResults.Ok(FutureLookProjection.Build(snapshot));
        }).WithName("GetFutureLook").WithSummary("Project the next 24 months of savings funding")
        .WithDescription("Read-only monthly allocation estimates, grouped by current term and purpose category, with immediate funding warnings.");
}
