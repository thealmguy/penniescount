using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Api.Holidays;
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
        using var earlierPayment = await client.PostAsJsonAsync($"/api/savings-items/{unpaidItem.Id}/payments",
            new RecordPaymentRequest { Amount = 25m, PaidOn = new(2027, 1, 3), Version = unpaidItem.Version });
        earlierPayment.EnsureSuccessStatusCode();

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
    public async Task Given_an_item_deadline_outside_holiday_coverage_When_loading_the_dashboard_Then_both_parts_of_the_schedule_count()
    {
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        }));
        using var client = fixedFactory.CreateClient();
        var before = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
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

        Assert.Equal(new DateOnly(2027, 1, 4), dashboard.NextAllocationDate);
        Assert.Equal(ContributionState.Scheduled, row.Status);
        Assert.Equal(4m, row.RequiredContribution);
        Assert.Equal(25, row.RemainingPayments);
        Assert.Equal(before.RequiredMonthlySavings + 4m, dashboard.RequiredMonthlySavings);
        Assert.False(dashboard.HolidayCalendar.IsStale);
        Assert.Null(dashboard.HolidayCalendar.Warning);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 4)]
    public async Task Given_a_failed_refresh_When_loading_the_dashboard_Then_cached_coverage_takes_precedence_over_fallback(
        bool hasCache, int nextDay)
    {
        const string warning = "Could not refresh the official holiday calendar.";
        var calendar = hasCache
            ? new BankHolidayCalendar(new HashSet<DateOnly> { new(2027, 1, 1) }, new(2027, 1, 1), new(2027, 12, 31))
            : null;
        await using var fixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock(1));
            services.RemoveAll<IBankHolidayProvider>();
            services.AddSingleton<IBankHolidayProvider>(new FixedHolidayProvider(new(calendar, null, true, warning)));
        }));
        using var client = fixedFactory.CreateClient();
        var before = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        using var creation = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "January goal",
            TargetAmount = 100m,
            StartDate = new(2027, 1, 1),
            TargetDate = new(2027, 1, 1)
        });
        creation.EnsureSuccessStatusCode();
        var item = (await creation.Content.ReadFromJsonAsync<SavingsItemResponse>())!;

        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var row = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == item.Id);

        Assert.Equal(new DateOnly(2027, 1, nextDay), dashboard.NextAllocationDate);
        Assert.Equal(hasCache ? ContributionState.NeededNow : ContributionState.Scheduled, row.Status);
        Assert.Equal(hasCache ? 0 : 1, row.RemainingPayments);
        Assert.Equal(100m, row.RequiredContribution);
        Assert.Equal(before.RequiredMonthlySavings + 100m, dashboard.RequiredMonthlySavings);
        Assert.True(dashboard.HolidayCalendar.IsStale);
        Assert.Equal(calendar?.CoveredThrough, dashboard.HolidayCalendar.CoveredThrough);
        Assert.Equal(warning, dashboard.HolidayCalendar.Warning);
    }

    private sealed class FixedClock(int day = 4) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, day, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FixedHolidayProvider(HolidayCalendarStatus status) : IBankHolidayProvider
    {
        public Task<HolidayCalendarStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(status);
    }
}
