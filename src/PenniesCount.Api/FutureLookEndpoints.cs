using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;
using PenniesCount.Contracts;
using PenniesCount.Core;

namespace PenniesCount.Api;

internal static class FutureLookEndpoints
{
    public static void MapFutureLook(this WebApplication app) =>
        app.MapGet("/api/future-look", async (SavingsDbContext db, TimeProvider clock,
            IBankHolidayProvider holidayProvider, CancellationToken ct) =>
        {
            var snapshot = await BudgetSnapshot.LoadAsync(db, clock, holidayProvider, ct);
            var calculator = new FundingForecast(snapshot.Schedule);
            var projections = snapshot.Items.Select(entity =>
            {
                var item = entity.ToResponse(snapshot.AsOf);
                return (Item: item, Forecast: calculator.Calculate(
                    new(item.TargetAmount, item.AllocatedAmount, item.StartDate, item.TargetDate),
                    snapshot.AsOf, entity.HasPaymentOn(snapshot.AsOf)));
            }).ToArray();
            var firstMonth = new DateOnly(snapshot.AsOf.Year, snapshot.AsOf.Month, 1);
            var months = Enumerable.Range(0, 24).Select(offset =>
            {
                var month = firstMonth.AddMonths(offset);
                var categories = Enum.GetValues<SavingsTerm>().Select(type => new ForecastCategoryResponse(type,
                    projections.Where(x => x.Item.Type == type).SelectMany(x => x.Forecast.Payments)
                        .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                        .Sum(x => x.Amount))).ToArray();
                return new ForecastMonthResponse(month, categories, categories.Sum(x => x.Amount));
            }).ToArray();
            var neededNow = projections.Where(x => x.Forecast.NeededNow).Select(x =>
                new ForecastAttentionResponse(x.Item.Id, x.Item.Name, x.Item.TargetAmount - x.Item.AllocatedAmount)).ToArray();
            return TypedResults.Ok(new FutureLookResponse(snapshot.AsOf, snapshot.Holidays, months, neededNow));
        }).WithName("GetFutureLook").WithSummary("Project the next 24 months of savings funding")
        .WithDescription("Read-only monthly allocation estimates, grouped by current category, with immediate funding warnings.");
}
