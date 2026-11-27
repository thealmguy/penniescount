using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Api.Data;
using PenniesCount.Api;
using PenniesCount.Api.Holidays;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class FutureLookScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Given_all_purpose_categories_and_legacy_items_When_forecasting_Then_both_groupings_reconcile_and_edits_move_funding()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = host.CreateClient();
        SavingsItemResponse? first = null;
        foreach (var category in Enum.GetValues<SavingsCategory>())
        {
            var item = await CreateAsync(client, $"Category {category}", 100m + 3m * (int)category, 0m,
                new(2027, 1, 1), new(2027, 4, 1), category);
            first ??= item;
        }
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
            db.SavingsItems.Add(new SavingsItem
            {
                Name = "Uncategorised legacy", TargetAmount = 90m, StartDate = new(2027, 1, 1), TargetDate = new(2027, 4, 1)
            });
            await db.SaveChangesAsync();
        }
        var removed = await CreateAsync(client, "Removed category example", 900m, 0m,
            new(2027, 1, 1), new(2027, 4, 1), SavingsCategory.EmergencyFund);
        using var removal = await client.PutAsJsonAsync($"/api/savings-items/{removed.Id}/removal",
            new ItemVersionRequest { Version = removed.Version });
        removal.EnsureSuccessStatusCode();
        var forecast = (await client.GetFromJsonAsync<FutureLookResponse>("/api/future-look"))!;
        Assert.All(forecast.Months, month =>
        {
            Assert.Equal(10, month.PurposeCategories.Length);
            Assert.Equal(Enum.GetValues<SavingsCategory>().Select(x => (SavingsCategory?)x).Append(null),
                month.PurposeCategories.Select(x => x.Category));
            Assert.Equal(month.Total, month.PurposeCategories.Sum(x => x.Amount));
            Assert.Equal(month.Categories.Sum(x => x.Amount), month.Total);
        });
        foreach (var category in Enum.GetValues<SavingsCategory>())
        {
            Assert.Equal(33.34m + (int)category, forecast.Months[1].PurposeCategories.Single(x => x.Category == category).Amount);
            Assert.Equal(33.32m + (int)category, forecast.Months[3].PurposeCategories.Single(x => x.Category == category).Amount);
        }
        Assert.Equal(30m, forecast.Months[1].PurposeCategories.Single(x => x.Category is null).Amount);
        Assert.All(forecast.Months.Skip(4), month => Assert.All(month.PurposeCategories, x => Assert.Equal(0m, x.Amount)));
        Assert.All(forecast.Months[0].PurposeCategories, x => Assert.Equal(0m, x.Amount));

        using var edit = await client.PutAsJsonAsync($"/api/savings-items/{first!.Id}/details",
            new UpdateSavingsItemDetailsRequest
            {
                Name = first.Name, TargetDate = first.TargetDate, Category = SavingsCategory.Transport, Version = first.Version
            });
        edit.EnsureSuccessStatusCode();
        var moved = (await client.GetFromJsonAsync<FutureLookResponse>("/api/future-look"))!;
        Assert.Equal(forecast.Months.Select(x => x.Total), moved.Months.Select(x => x.Total));
        Assert.Equal(0m, moved.Months[1].PurposeCategories.Single(x => x.Category == SavingsCategory.EmergencyFund).Amount);
        Assert.Equal(68.68m, moved.Months[1].PurposeCategories.Single(x => x.Category == SavingsCategory.Transport).Amount);
    }

    [Fact]
    public async Task Given_active_goals_When_reading_future_look_Then_it_returns_24_months_with_capped_payments_and_attention()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        var standard = await CreateAsync(client, "Three dates", 100m, 0m, new(2027, 1, 1), new(2027, 4, 1));
        await CreateAsync(client, "Future start", 90m, 0m, new(2027, 2, 2), new(2027, 4, 1));
        await CreateAsync(client, "Impossible future start", 100m, 25m, new(2027, 2, 2), new(2027, 2, 28));
        var longTerm = await CreateAsync(client, "Beyond the chart", 360m, 0m, new(2027, 1, 1), new(2030, 1, 1));
        var removed = await CreateAsync(client, "Removed goal", 100m, 0m, new(2027, 1, 1), new(2027, 4, 1));
        using var removal = await client.PutAsJsonAsync($"/api/savings-items/{removed.Id}/removal",
            new ItemVersionRequest { Version = removed.Version });
        removal.EnsureSuccessStatusCode();

        using var response = await client.GetAsync("/api/future-look");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forecast = (await response.Content.ReadFromJsonAsync<FutureLookResponse>())!;
        Assert.Equal(new DateOnly(2027, 1, 15), forecast.AsOfDate);
        Assert.Equal(24, forecast.Months.Length);
        Assert.Equal(new DateOnly(2027, 1, 1), forecast.Months[0].Month);
        Assert.Equal(new DateOnly(2028, 12, 1), forecast.Months[^1].Month);
        Assert.Equal(75m, forecast.Months[0].Total);
        Assert.All(forecast.Months, month =>
        {
            Assert.Equal(4, month.Categories.Length);
            Assert.Equal(Enum.GetValues<SavingsTerm>(), month.Categories.Select(x => x.Type));
            Assert.Equal(month.Categories.Sum(x => x.Amount), month.Total);
            Assert.Equal(month.Total, month.PurposeCategories.Single(x => x.Category == SavingsCategory.Other).Amount);
            Assert.Equal(month.Total, month.PurposeCategories.Sum(x => x.Amount));
        });
        var shortTerm = forecast.Months.Select(x => x.Categories.Single(y => y.Type == SavingsTerm.Short)).ToArray();
        Assert.Equal(33.34m, shortTerm[1].Amount);
        Assert.Equal(78.34m, shortTerm[2].Amount);
        Assert.Equal(78.32m, shortTerm[3].Amount);
        Assert.Equal(10m, forecast.Months[^1].Categories.Single(x => x.Type == SavingsTerm.Medium).Amount);
        var immediate = Assert.Single(forecast.NeededNow);
        Assert.Equal("Impossible future start", immediate.Name);
        Assert.Equal(75m, immediate.Amount);
        Assert.DoesNotContain(forecast.NeededNow, x => x.ItemId == longTerm.Id || x.ItemId == standard.Id);
    }

    [Fact]
    public async Task Given_funded_with_drawdown_and_removed_items_When_reading_dashboard_summaries_Then_amounts_reconcile()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        var item = await CreateAsync(client, "Drawdown summary", 100m, 100m, new(2027, 1, 1), new(2027, 4, 1));
        using var drawdown = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/drawdowns",
            new SaveDrawdownRequest { Amount = 25m, SpentOn = new(2027, 1, 15), Version = item.Version });
        drawdown.EnsureSuccessStatusCode();
        var afterDrawdown = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var summary = afterDrawdown.Summaries.Single(x => x.Type == SavingsTerm.Short);
        Assert.Equal(100m, summary.TargetAmount);
        Assert.Equal(100m, summary.FundedAmount);
        Assert.Equal(25m, summary.DrawnDownAmount);
        Assert.Equal(75m, summary.AvailableAmount);
        Assert.Equal(afterDrawdown.TotalAllocated, afterDrawdown.Summaries.Single(x => x.Type is null).AvailableAmount);
        Assert.Equal(5, afterDrawdown.Summaries.Length);

        var updated = (await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"))!;
        using var removal = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/removal",
            new ItemVersionRequest { Version = updated.Version });
        removal.EnsureSuccessStatusCode();
        var afterRemoval = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(0m, afterRemoval.Summaries.Single(x => x.Type is null).TargetAmount);
        Assert.Equal(0m, afterRemoval.Summaries.Single(x => x.Type is null).AvailableAmount);
    }

    [Fact]
    public async Task Given_a_payment_today_When_reversed_Then_the_forecast_restores_todays_allocation()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new AllocationDayClock());
        }));
        using var client = fixedFactory.CreateClient();
        var item = await CreateAsync(client, "Payment example", 100m, 0m, new(2027, 1, 1), new(2027, 4, 1));
        using var payment = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/payments",
            new RecordPaymentRequest { Amount = 10m, PaidOn = new(2027, 1, 4), Version = item.Version });
        payment.EnsureSuccessStatusCode();
        var entry = (await payment.Content.ReadFromJsonAsync<LedgerEntryResponse>())!;

        var paidForecast = (await client.GetFromJsonAsync<FutureLookResponse>("/api/future-look"))!;
        Assert.Equal(0m, paidForecast.Months[0].Total);
        Assert.Equal(30m, paidForecast.Months[1].Total);
        Assert.Equal(0m, paidForecast.Months[0].PurposeCategories.Single(x => x.Category == SavingsCategory.Other).Amount);
        Assert.Equal(30m, paidForecast.Months[1].PurposeCategories.Single(x => x.Category == SavingsCategory.Other).Amount);

        var updated = (await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"))!;
        using var reversal = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/ledger/{entry.Id}/reversal",
            new ItemVersionRequest { Version = updated.Version });
        reversal.EnsureSuccessStatusCode();
        var reversedForecast = (await client.GetFromJsonAsync<FutureLookResponse>("/api/future-look"))!;
        Assert.Equal(25m, reversedForecast.Months[0].Total);
        Assert.Equal(25m, reversedForecast.Months[1].Total);
        Assert.Equal(25m, reversedForecast.Months[0].PurposeCategories.Single(x => x.Category == SavingsCategory.Other).Amount);
    }

    [Fact]
    public async Task Given_goals_in_every_category_When_forecasting_Then_each_month_keeps_the_as_of_category()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        await CreateAsync(client, "Historic goal", 11m, 0m, new(2027, 1, 1), new(2027, 1, 14));
        await CreateAsync(client, "Short goal", 30m, 0m, new(2027, 1, 1), new(2027, 4, 1));
        await CreateAsync(client, "Medium goal", 150m, 0m, new(2027, 1, 1), new(2028, 4, 3));
        await CreateAsync(client, "Long goal", 720m, 0m, new(2027, 1, 1), new(2033, 1, 3));

        var forecast = (await client.GetFromJsonAsync<FutureLookResponse>("/api/future-look"))!;

        Assert.Equal(11m, forecast.Months[0].Categories.Single(x => x.Type == SavingsTerm.Past).Amount);
        Assert.Equal(11m, forecast.Months[0].Total);
        Assert.Equal(10m, forecast.Months[1].Categories.Single(x => x.Type == SavingsTerm.Short).Amount);
        Assert.Equal(10m, forecast.Months[1].Categories.Single(x => x.Type == SavingsTerm.Medium).Amount);
        Assert.Equal(10m, forecast.Months[1].Categories.Single(x => x.Type == SavingsTerm.Long).Amount);
        Assert.Equal(0m, forecast.Months[1].Categories.Single(x => x.Type == SavingsTerm.Past).Amount);
        Assert.Equal(30m, forecast.Months[1].Total);
        Assert.Equal(10m, forecast.Months[12].Categories.Single(x => x.Type == SavingsTerm.Medium).Amount);
    }

    [Fact]
    public void Given_an_unavailable_contribution_When_summarizing_Then_only_affected_totals_are_unavailable()
    {
        var item = new SavingsItemResponse(Guid.NewGuid(), "Example", SavingsTerm.Short, 100m, 20m,
            new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=") { DrawnDownAmount = 5m, AvailableAmount = 15m };
        var groups = new[]
        {
            new SavingsGroupResponse(SavingsTerm.Short,
                [new DashboardItemResponse(item, 80m, null, null, ContributionState.Unavailable)]),
            new SavingsGroupResponse(SavingsTerm.Medium, [])
        };

        var summaries = DashboardEndpoints.Summarize(groups);

        Assert.Equal(5, summaries.Length);
        var shortTerm = summaries.Single(x => x.Type == SavingsTerm.Short);
        Assert.Equal(100m, shortTerm.TargetAmount);
        Assert.Equal(20m, shortTerm.FundedAmount);
        Assert.Equal(5m, shortTerm.DrawnDownAmount);
        Assert.Equal(15m, shortTerm.AvailableAmount);
        Assert.Null(shortTerm.RequiredMonthlyContribution);
        Assert.Equal(0m, summaries.Single(x => x.Type == SavingsTerm.Medium).RequiredMonthlyContribution);
        Assert.Null(summaries.Single(x => x.Type is null).RequiredMonthlyContribution);
    }

    [Fact]
    public void Given_multiple_categories_When_summarizing_Then_each_row_and_the_grand_total_only_sum_their_items()
    {
        var first = new SavingsItemResponse(Guid.NewGuid(), "First", SavingsTerm.Short, 100m, 40m,
            new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=") { DrawnDownAmount = 10m, AvailableAmount = 30m };
        var second = first with
        {
            Id = Guid.NewGuid(), Type = SavingsTerm.Medium, TargetAmount = 200m,
            AllocatedAmount = 80m, DrawnDownAmount = 25m, AvailableAmount = 55m
        };
        var groups = new[]
        {
            new SavingsGroupResponse(SavingsTerm.Short,
                [new DashboardItemResponse(first, 60m, 20m, 3, ContributionState.Scheduled)]),
            new SavingsGroupResponse(SavingsTerm.Medium,
                [new DashboardItemResponse(second, 120m, 30m, 4, ContributionState.Scheduled)])
        };

        var summaries = DashboardEndpoints.Summarize(groups);

        Assert.Equal(new SavingsSummaryResponse(SavingsTerm.Short, 100m, 40m, 10m, 30m, 20m),
            summaries.Single(x => x.Type == SavingsTerm.Short));
        Assert.Equal(new SavingsSummaryResponse(SavingsTerm.Medium, 200m, 80m, 25m, 55m, 30m),
            summaries.Single(x => x.Type == SavingsTerm.Medium));
        Assert.Equal(new SavingsSummaryResponse(SavingsTerm.Long, 0m, 0m, 0m, 0m, 0m),
            summaries.Single(x => x.Type == SavingsTerm.Long));
        Assert.Equal(new SavingsSummaryResponse(null, 300m, 120m, 35m, 85m, 50m),
            summaries.Single(x => x.Type is null));
    }

    private static async Task<SavingsItemResponse> CreateAsync(HttpClient client, string name, decimal target,
        decimal allocated, DateOnly start, DateOnly deadline, SavingsCategory category = SavingsCategory.Other)
    {
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = name, TargetAmount = target, AllocatedAmount = allocated, StartDate = start, TargetDate = deadline,
            Category = category
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class AllocationDayClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
