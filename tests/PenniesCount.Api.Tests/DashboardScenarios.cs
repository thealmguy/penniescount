using System.Data.SqlTypes;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Api.Holidays;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class DashboardScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
{
    [Fact]
    public async Task Given_pooled_accounts_and_items_When_balances_or_allocations_change_Then_the_dashboard_is_consistent()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        var empty = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(0m, empty.TotalSavings);
        Assert.Equal(0m, empty.RequiredMonthlySavings);
        Assert.Equal(new DateOnly(2027, 2, 1), empty.NextAllocationDate);
        Assert.Equal(HolidayCalendarStatus.Source, empty.HolidayCalendar.Source);
        Assert.Equal(new DateOnly(2028, 12, 31), empty.HolidayCalendar.CoveredThrough);
        Assert.Equal(Enum.GetValues<SavingsTerm>(), empty.Groups.Select(x => x.Type));
        Assert.All(empty.Groups, group => Assert.Empty(group.Items));
        Assert.Equal(5, empty.Summaries.Length);
        Assert.All(empty.Summaries, summary =>
        {
            Assert.Equal(0m, summary.TargetAmount);
            Assert.Equal(0m, summary.RequiredMonthlyContribution);
        });
        Assert.Equal(Enum.GetValues<SavingsCategory>().Select(x => (SavingsCategory?)x).Append(null),
            empty.PurposeSummaries.Select(x => x.Category));
        Assert.All(empty.PurposeSummaries, summary =>
            Assert.Equal(new(summary.Category, 0m, 0m, 0m, 0m, 0m), summary));

        var account = await AccountScenarios.CreateAsync(client, 50m);
        await AccountScenarios.CreateAsync(client, 100m);
        var item = await SavingsItemScenarios.CreateAsync(client, 25m);
        using var future = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Future example", TargetAmount = 100m, AllocatedAmount = 50m,
            Category = SavingsCategory.Other,
            StartDate = new(2027, 2, 1), TargetDate = new(2027, 3, 1)
        });
        future.EnsureSuccessStatusCode();
        using var urgent = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Urgent example", TargetAmount = 100m,
            Category = SavingsCategory.Other,
            StartDate = new(2027, 1, 1), TargetDate = new(2027, 1, 31)
        });
        urgent.EnsureSuccessStatusCode();
        foreach (var name in new[] { "Tied B", "Tied A", "Tied A" })
        {
            using var tied = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
            {
                Name = name, TargetAmount = 100m, StartDate = new(2027, 2, 1), TargetDate = new(2027, 3, 1),
                Category = SavingsCategory.Other
            });
            tied.EnsureSuccessStatusCode();
        }

        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(new DateOnly(2027, 1, 15), dashboard.AsOfDate);
        Assert.Equal(150m, dashboard.TotalSavings);
        Assert.Equal(75m, dashboard.TotalAllocated);
        Assert.Equal(75m, dashboard.UnallocatedBalance);
        Assert.Equal(125m, dashboard.RequiredMonthlySavings);
        Assert.Equal(new DateOnly(2027, 2, 1), dashboard.NextAllocationDate);
        Assert.Equal(ContributionState.NeededNow, dashboard.Groups[0].Items.Single(x => x.Item.Name == "Urgent example").Status);
        Assert.Equal(ContributionState.NotStarted, dashboard.Groups[0].Items.Single(x => x.Item.Name == "Future example").Status);
        Assert.Equal(dashboard.RequiredMonthlySavings, dashboard.Groups.SelectMany(x => x.Items).Sum(x => x.RequiredContribution));
        var grandTotal = dashboard.Summaries.Single(x => x.Type is null);
        Assert.Equal(600m, grandTotal.TargetAmount);
        Assert.Equal(75m, grandTotal.FundedAmount);
        Assert.Equal(0m, grandTotal.DrawnDownAmount);
        Assert.Equal(dashboard.TotalAllocated, grandTotal.AvailableAmount);
        Assert.Equal(dashboard.RequiredMonthlySavings, grandTotal.RequiredMonthlyContribution);
        var purposeTotal = dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.Other);
        Assert.Equal(new(SavingsCategory.Other, 600m, 75m, 0m, 75m, 125m), purposeTotal);
        Assert.All(dashboard.Groups, group => Assert.Equal(
            group.Items.OrderBy(x => x.Item.TargetDate).ThenBy(x => x.Item.Name).ThenBy(x => new SqlGuid(x.Item.Id)),
            group.Items));
        Assert.Equal(["Tied A", "Tied A", "Tied B"], dashboard.Groups.SelectMany(x => x.Items)
            .Select(x => x.Item.Name).Where(x => x.StartsWith("Tied", StringComparison.Ordinal)));

        using var funds = await client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
            new UpdateBalanceRequest { TotalFunds = 0m, Version = account.Version });
        funds.EnsureSuccessStatusCode();
        var afterBalance = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(75m, afterBalance.TotalAllocated);
        Assert.Equal(25m, afterBalance.UnallocatedBalance);

        using var allocation = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
            new UpdateAllocationRequest { AllocatedAmount = 100m, Version = item.Version });
        allocation.EnsureSuccessStatusCode();
        var afterAllocation = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(100m, afterAllocation.TotalSavings);
        Assert.Equal(150m, afterAllocation.TotalAllocated);
        Assert.Equal(-50m, afterAllocation.UnallocatedBalance);
        Assert.Equal(100m, afterAllocation.RequiredMonthlySavings);
        Assert.Equal(150m, afterAllocation.PurposeSummaries.Single(x => x.Category == SavingsCategory.Other).AvailableAmount);
    }

    [Fact]
    public void Given_purposes_across_savings_terms_When_summarizing_Then_each_purpose_reconciles_all_five_columns()
    {
        var groups = new[]
        {
            new SavingsGroupResponse(SavingsTerm.Short, [Row(SavingsCategory.Transport, 100m, 40m, 10m, 20m),
                Row(null, 50m, 20m, 5m, 10m)]),
            new SavingsGroupResponse(SavingsTerm.Long, [Row(SavingsCategory.Transport, 200m, 80m, 25m, 30m),
                Row(SavingsCategory.HolidaysAndTravel, 300m, 150m, 100m, 40m)])
        };
        var dashboard = SummarizedBudget(groups);

        Assert.Equal(new(SavingsCategory.Transport, 300m, 120m, 35m, 85m, 50m),
            dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.Transport));
        Assert.Equal(new(null, 50m, 20m, 5m, 15m, 10m),
            dashboard.PurposeSummaries.Single(x => x.Category is null));
        Assert.Equal(new(SavingsCategory.HolidaysAndTravel, 300m, 150m, 100m, 50m, 40m),
            dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.HolidaysAndTravel));
        Assert.Equal(new(SavingsCategory.Other, 0m, 0m, 0m, 0m, 0m),
            dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.Other));
        var grand = dashboard.Summaries.Single(x => x.Type is null);
        Assert.Equal(grand.TargetAmount, dashboard.PurposeSummaries.Sum(x => x.TargetAmount));
        Assert.Equal(grand.FundedAmount, dashboard.PurposeSummaries.Sum(x => x.FundedAmount));
        Assert.Equal(grand.DrawnDownAmount, dashboard.PurposeSummaries.Sum(x => x.DrawnDownAmount));
        Assert.Equal(grand.AvailableAmount, dashboard.PurposeSummaries.Sum(x => x.AvailableAmount));
        Assert.Equal(grand.RequiredMonthlyContribution, dashboard.PurposeSummaries.Sum(x => x.RequiredMonthlyContribution));
    }

    [Fact]
    public void Given_an_unavailable_purpose_contribution_When_summarizing_Then_other_purposes_remain_known()
    {
        var groups = new[] { new SavingsGroupResponse(SavingsTerm.Short,
            [Row(SavingsCategory.Transport, 100m, 40m, 10m, 20m),
                Row(SavingsCategory.Transport, 200m, 80m, 25m, null),
                Row(null, 50m, 20m, 5m, null),
                Row(SavingsCategory.Other, 300m, 150m, 100m, 40m)]) };
        var dashboard = SummarizedBudget(groups);

        var transport = dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.Transport);
        Assert.Equal(new(SavingsCategory.Transport, 300m, 120m, 35m, 85m, null), transport);
        Assert.Null(dashboard.PurposeSummaries.Single(x => x.Category is null).RequiredMonthlyContribution);
        Assert.Equal(40m, dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.Other).RequiredMonthlyContribution);
        Assert.Equal(0m, dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.EmergencyFund).RequiredMonthlyContribution);
    }

    private static DashboardItemResponse Row(SavingsCategory? category, decimal target, decimal funded,
        decimal drawn, decimal? contribution) =>
        new(new(Guid.NewGuid(), "Fictional goal", SavingsTerm.Short, target, funded,
            new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=")
            { Category = category, DrawnDownAmount = drawn, AvailableAmount = funded - drawn },
            target - funded, contribution, null, ContributionState.Scheduled);

    private static DashboardResponse SummarizedBudget(SavingsGroupResponse[] groups) =>
        new(new(2027, 1, 15), 0m, 0m, 0m, 0m, null, new("Test", null, null, false, null), groups)
        {
            Summaries = DashboardEndpoints.Summarize(groups),
            PurposeSummaries = DashboardEndpoints.SummarizePurposes(groups)
        };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
