using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class DashboardSummaryScenarios
{
    private static DashboardItemResponse Row(SavingsTerm term, SavingsCategory? category, decimal target,
        decimal funded, decimal drawn, decimal? required) =>
        new(new SavingsItemResponse(Guid.NewGuid(), "Goal", term, target, funded, new(2027, 1, 1),
                new(2027, 6, 1), "AAAAAAAAAAE=")
            {
                Category = category, DrawnDownAmount = drawn, AvailableAmount = funded - drawn
            }, target - funded, required, null, ContributionState.Scheduled);

    private static SavingsGroupResponse[] Groups() =>
    [
        new(SavingsTerm.Short, [
            Row(SavingsTerm.Short, SavingsCategory.Transport, 100m, 60m, 10m, 5m),
            Row(SavingsTerm.Short, SavingsCategory.Transport, 50m, 20m, 5m, 3m)
        ]),
        new(SavingsTerm.Medium, [
            Row(SavingsTerm.Medium, SavingsCategory.Other, 300m, 40m, 0m, 7m),
            Row(SavingsTerm.Medium, null, 200m, 30m, 2m, null)
        ]),
        new(SavingsTerm.Long, []),
        new(SavingsTerm.Past, [])
    ];

    [Fact]
    public void Given_groups_by_term_When_summarised_Then_each_term_and_the_overall_total_reconcile()
    {
        Assert.Equal(
        [
            new SavingsSummaryResponse(SavingsTerm.Short, 150m, 80m, 15m, 65m, 8m),
            new SavingsSummaryResponse(SavingsTerm.Medium, 500m, 70m, 2m, 68m, null),
            new SavingsSummaryResponse(SavingsTerm.Long, 0m, 0m, 0m, 0m, 0m),
            new SavingsSummaryResponse(SavingsTerm.Past, 0m, 0m, 0m, 0m, 0m),
            new SavingsSummaryResponse(null, 650m, 150m, 17m, 133m, null)
        ], DashboardEndpoints.Summarize(Groups()));
    }

    [Fact]
    public void Given_items_with_purposes_When_summarised_Then_every_purpose_is_listed_with_uncategorised_last()
    {
        var summaries = DashboardEndpoints.SummarizePurposes(Groups());

        Assert.Equal(Enum.GetValues<SavingsCategory>().Select(x => (SavingsCategory?)x).Append(null),
            summaries.Select(x => x.Category));
        Assert.Equal(new SavingsPurposeSummaryResponse(SavingsCategory.Transport, 150m, 80m, 15m, 65m, 8m),
            summaries.Single(x => x.Category == SavingsCategory.Transport));
        Assert.Equal(new SavingsPurposeSummaryResponse(SavingsCategory.Other, 300m, 40m, 0m, 40m, 7m),
            summaries.Single(x => x.Category == SavingsCategory.Other));
        Assert.Equal(new SavingsPurposeSummaryResponse(null, 200m, 30m, 2m, 28m, null), summaries[^1]);
        Assert.Equal(new SavingsPurposeSummaryResponse(SavingsCategory.HouseAndGarden, 0m, 0m, 0m, 0m, 0m),
            summaries.Single(x => x.Category == SavingsCategory.HouseAndGarden));
    }
}
