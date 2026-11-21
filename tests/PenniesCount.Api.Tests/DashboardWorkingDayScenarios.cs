using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class DashboardWorkingDayScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
{
    [Fact]
    public async Task Given_a_payment_on_the_first_working_day_When_loading_the_dashboard_Then_only_that_items_payment_is_excluded()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        var item = await SavingsItemScenarios.CreateAsync(client, 25m);
        using var payment = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/payments",
            new RecordPaymentRequest { Amount = 25m, PaidOn = new(2027, 1, 4), Version = item.Version });
        payment.EnsureSuccessStatusCode();
        var unpaidItem = await SavingsItemScenarios.CreateAsync(client, 25m);

        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var row = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == item.Id);
        var unpaidRow = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == unpaidItem.Id);

        Assert.Equal(new DateOnly(2027, 1, 4), dashboard.NextAllocationDate);
        Assert.True(row.PaidToday);
        Assert.Equal(3, row.RemainingPayments);
        Assert.Equal(16.67m, row.RequiredContribution);
        Assert.False(unpaidRow.PaidToday);
        Assert.Equal(4, unpaidRow.RemainingPayments);
    }

    [Fact]
    public async Task Given_an_item_deadline_outside_holiday_coverage_When_loading_the_dashboard_Then_only_its_recommendation_is_unavailable()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        using var creation = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Beyond holiday coverage",
            TargetAmount = 100m,
            StartDate = new(2027, 1, 1),
            TargetDate = new(2029, 1, 1)
        });
        creation.EnsureSuccessStatusCode();
        var item = (await creation.Content.ReadFromJsonAsync<SavingsItemResponse>())!;

        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var row = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == item.Id);

        Assert.Equal(ContributionState.Unavailable, row.Status);
        Assert.Null(row.RequiredContribution);
        Assert.Null(row.RemainingPayments);
        Assert.Null(dashboard.RequiredMonthlySavings);
        Assert.False(dashboard.HolidayCalendar.IsStale);
        Assert.Null(dashboard.HolidayCalendar.Warning);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
