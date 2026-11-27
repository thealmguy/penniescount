using PenniesCount.Api.Data;
using PenniesCount.Contracts;
using PenniesCount.Core;

namespace PenniesCount.Api.Tests;

public sealed class FutureLookProjectionScenarios
{
    [Fact]
    public void Given_goals_across_terms_and_purposes_When_projected_Then_month_totals_reconcile()
    {
        var asOf = new DateOnly(2027, 1, 15);
        var first = Goal("First", 60m, asOf, new(2027, 4, 1), SavingsCategory.Transport);
        var second = Goal("Second", 30m, asOf, new(2027, 4, 1), SavingsCategory.Transport);
        var legacy = Goal("Legacy", 90m, asOf, new(2027, 4, 1), null);
        var longer = Goal("Longer", 150m, asOf, new(2028, 4, 3), SavingsCategory.Other);
        var late = Goal("Late", 100m, asOf, asOf.AddDays(-1), SavingsCategory.Other);
        late.StartDate = asOf.AddDays(-2);
        late.Record(LedgerEntryType.OpeningFunding, 25m, late.StartDate, null,
            new DateTimeOffset(2027, 1, 13, 0, 0, 0, TimeSpan.Zero));

        var forecast = FutureLookProjection.Build(Snapshot(asOf, first, second, legacy, longer, late));

        Assert.Equal(asOf, forecast.AsOfDate);
        Assert.Equal(24, forecast.Months.Length);
        Assert.Equal(new DateOnly(2027, 1, 1), forecast.Months[0].Month);
        Assert.Equal(new DateOnly(2028, 12, 1), forecast.Months[^1].Month);
        Assert.Equal(new ForecastAttentionResponse(late.Id, late.Name, 75m), Assert.Single(forecast.NeededNow));
        Assert.All(forecast.Months, month =>
        {
            Assert.Equal(Enum.GetValues<SavingsTerm>(), month.Categories.Select(x => x.Type));
            Assert.Equal(Enum.GetValues<SavingsCategory>().Select(x => (SavingsCategory?)x).Append(null),
                month.PurposeCategories.Select(x => x.Category));
            Assert.Equal(month.Total, month.Categories.Sum(x => x.Amount));
            Assert.Equal(month.Total, month.PurposeCategories.Sum(x => x.Amount));
        });
        var firstFebruary = forecast.Months[1];
        Assert.Equal((70m, 60m, 10m), (firstFebruary.Total,
            firstFebruary.Categories.Single(x => x.Type == SavingsTerm.Short).Amount,
            firstFebruary.Categories.Single(x => x.Type == SavingsTerm.Medium).Amount));
        Assert.Equal(30m, firstFebruary.PurposeCategories.Single(x => x.Category == SavingsCategory.Transport).Amount);
        Assert.Equal(30m, firstFebruary.PurposeCategories.Single(x => x.Category is null).Amount);
        Assert.Equal(10m, forecast.Months[13].Total);
        Assert.Equal(0m, forecast.Months[^1].Total);
    }

    [Fact]
    public void Given_a_payment_on_allocation_day_When_reversed_Then_todays_projection_is_restored()
    {
        var asOf = new DateOnly(2027, 1, 4);
        var now = new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        var item = Goal("Goal", 100m, asOf, new(2027, 4, 1), SavingsCategory.Other);
        var payment = item.Record(LedgerEntryType.AllocationPayment, 10m, asOf, null, now);
        var snapshot = Snapshot(asOf, item);

        var paid = FutureLookProjection.Build(snapshot);
        item.Record(LedgerEntryType.Reversal, payment.Amount, asOf, null, now, reverses: payment.Id);
        var reversed = FutureLookProjection.Build(snapshot);

        Assert.Equal([0m, 30m, 30m, 30m], paid.Months.Take(4).Select(x => x.Total));
        Assert.Equal([25m, 25m, 25m, 25m], reversed.Months.Take(4).Select(x => x.Total));
    }

    private static SavingsItem Goal(string name, decimal amount, DateOnly start, DateOnly target,
        SavingsCategory? category) =>
        new() { Name = name, TargetAmount = amount, StartDate = start, TargetDate = target, Category = category };

    private static BudgetSnapshot Snapshot(DateOnly asOf, params SavingsItem[] items) =>
        new(asOf, 0m, items, new WorkingDaySchedule(new(2027, 1, 1), new(2028, 12, 31), [new(2027, 1, 1)]),
            new("test", null, null, false, null));
}
