using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        Assert.Equal(Enum.GetValues<SavingsTerm>(), empty.Groups.Select(x => x.Type));
        Assert.All(empty.Groups, group => Assert.Empty(group.Items));

        var account = await AccountScenarios.CreateAsync(client, 50m);
        await AccountScenarios.CreateAsync(client, 100m);
        var item = await SavingsItemScenarios.CreateAsync(client, 25m);
        using var future = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Future example", TargetAmount = 100m, AllocatedAmount = 50m,
            StartDate = new(2027, 2, 1), TargetDate = new(2027, 3, 1)
        });
        future.EnsureSuccessStatusCode();
        using var urgent = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Urgent example", TargetAmount = 100m,
            StartDate = new(2027, 1, 1), TargetDate = new(2027, 1, 31)
        });
        urgent.EnsureSuccessStatusCode();

        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(new DateOnly(2027, 1, 15), dashboard.AsOfDate);
        Assert.Equal(150m, dashboard.TotalSavings);
        Assert.Equal(75m, dashboard.TotalAllocated);
        Assert.Equal(75m, dashboard.UnallocatedBalance);
        Assert.Equal(125m, dashboard.RequiredMonthlySavings);
        Assert.Equal(ContributionState.NeededNow, dashboard.Groups[0].Items.Single(x => x.Item.Name == "Urgent example").Status);
        Assert.Equal(ContributionState.NotStarted, dashboard.Groups[0].Items.Single(x => x.Item.Name == "Future example").Status);
        Assert.Equal(dashboard.RequiredMonthlySavings, dashboard.Groups.SelectMany(x => x.Items).Sum(x => x.RequiredContribution));
        Assert.All(dashboard.Groups, group => Assert.Equal(
            group.Items.OrderBy(x => x.Item.TargetDate).ThenBy(x => x.Item.Name).ThenBy(x => x.Item.Id), group.Items));

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
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
