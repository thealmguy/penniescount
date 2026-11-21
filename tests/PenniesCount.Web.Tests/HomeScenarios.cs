using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PenniesCount.Contracts;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class HomeScenarios
{
    [Fact]
    public void Given_an_unavailable_api_When_opening_home_Then_it_shows_an_error_not_zero_balances()
    {
        using var context = CreateContext(new ReplyHandler(_ => throw new HttpRequestException()), TimeProvider.System);

        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("unavailable", page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("#total-savings"));
    }

    [Fact]
    public void Given_a_loaded_budget_When_refresh_fails_Then_previous_figures_are_explicitly_marked_stale()
    {
        var fail = false;
        using var context = CreateContext(new ReplyHandler(_ => fail
            ? throw new HttpRequestException() : Json(Budget(new(2027, 1, 15), 25m))), TimeProvider.System);
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Contains("25.00", page.Find("#monthly-required").TextContent));
        fail = true;

        page.FindAll("button").Single(x => x.TextContent == "Refresh budget").Click();

        page.WaitForAssertion(() => Assert.Contains("last successful refresh", page.Markup));
        Assert.Contains("25.00", page.Find("#monthly-required").TextContent);
    }

    [Fact]
    public void Given_an_open_budget_When_the_local_date_rolls_over_Then_it_fetches_new_api_calculations_without_waiting_in_real_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 1, 31, 23, 59, 45, TimeSpan.Zero));
        var requests = 0;
        using var context = CreateContext(new ReplyHandler(_ =>
        {
            requests++;
            var date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return Json(Budget(date, date.Month == 1 ? 25m : 37.50m));
        }), clock);
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Contains("25.00", page.Find("#monthly-required").TextContent));

        clock.Advance(TimeSpan.FromSeconds(30));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("01 Feb 2027", page.Find("#budget-date").TextContent);
            Assert.Contains("37.50", page.Find("#monthly-required").TextContent);
            Assert.Equal(2, requests);
        });
    }

    [Fact]
    public void Given_a_budget_When_opening_home_Then_it_shows_category_totals_and_links_to_item_management()
    {
        using var context = CreateContext(new ReplyHandler(_ => Json(Budget(new(2027, 1, 15), 25m))), TimeProvider.System);
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll("#savings-totals tbody tr").Count));
        Assert.Equal("Historic", page.Find("#savings-totals tbody tr[data-category='Past'] th").TextContent);
        Assert.Contains("100.00", page.Find("#savings-totals tbody tr[data-category='Short'] td").TextContent);
        Assert.Contains("View savings items", page.Markup);
        Assert.DoesNotContain("Update allocation", page.Markup);
    }

    [Fact]
    public void Given_no_savings_items_When_opening_home_Then_it_shows_the_empty_budget()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReadyHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("No savings items yet", page.Markup));
    }

    [Fact]
    public void Given_missing_holiday_coverage_When_opening_home_Then_it_shows_weekday_estimates_and_the_warning()
    {
        var estimate = new DashboardItemResponse(Example, 75m, 25m, 3, ContributionState.Scheduled);
        var budget = Budget(new(2027, 1, 15), 25m) with
        {
            NextAllocationDate = new(2027, 2, 1),
            HolidayCalendar = new("GOV.UK bank holidays (England and Wales)", null,
                null, true, "The official holiday calendar is unavailable."),
            Groups = [new(SavingsTerm.Short, [estimate]), new(SavingsTerm.Medium, []), new(SavingsTerm.Long, [])]
        };
        using var context = CreateContext(new ReplyHandler(_ => Json(budget)), TimeProvider.System);

        var page = context.Render<Home>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("25.00", page.Find("#monthly-required").TextContent);
            Assert.Equal("01 Feb 2027", page.Find("#next-allocation-date").TextContent);
            Assert.Contains("The official holiday calendar is unavailable.", page.Markup);
            Assert.Contains("25.00", page.Find("#savings-totals").TextContent);
        });
    }

    [Fact]
    public void Given_a_goal_deadline_passes_When_the_date_rolls_over_Then_home_refreshes_its_category_summary()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 4, 15, 23, 59, 45, TimeSpan.Zero));
        using var context = CreateContext(new ReplyHandler(_ =>
        {
            var date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var type = date.Day == 15 ? SavingsTerm.Short : SavingsTerm.Past;
            var row = new DashboardItemResponse(Example with { Type = type }, 75m, 75m, 0, ContributionState.NeededNow);
            var groups = Enum.GetValues<SavingsTerm>().Select(category =>
                new SavingsGroupResponse(category, category == type ? [row] : [])).ToArray();
            return Json(Budget(date, 75m) with { Groups = groups, Summaries = Summaries(groups, 75m) });
        }), clock);
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Contains("100.00", page.Find("#savings-totals tbody tr[data-category='Short']").TextContent));

        clock.Advance(TimeSpan.FromSeconds(30));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("16 Apr 2027", page.Find("#budget-date").TextContent);
            Assert.Contains("Historic", page.Find("#savings-totals tbody tr[data-category='Past'] th").TextContent);
            Assert.Contains("75.00", page.Find("#savings-totals tbody tr[data-category='Past']").TextContent);
            Assert.Contains("75.00", page.Find("#monthly-required").TextContent);
        });
    }

    private sealed class ReadyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new DashboardResponse(new(2027, 1, 15), 0m, 0m, 0m, 0m,
                    new(2027, 2, 1), CurrentHolidays,
                                [new(SavingsTerm.Short, []), new(SavingsTerm.Medium, []), new(SavingsTerm.Long, []), new(SavingsTerm.Past, [])]))
            });
    }

    private static readonly SavingsItemResponse Example = new(Guid.NewGuid(), "Example goal", SavingsTerm.Short,
        100m, 25m, new(2027, 1, 1), new(2027, 4, 15), "AAAAAAAAAAA=") { AvailableAmount = 25m };

    private static readonly HolidayCalendarResponse CurrentHolidays = new(
        "GOV.UK bank holidays (England and Wales)", new(2028, 12, 31),
        new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), false, null);

    private static DashboardResponse Budget(DateOnly date, decimal monthly)
    {
        var groups = new[]
        {
            new SavingsGroupResponse(SavingsTerm.Short, [new DashboardItemResponse(Example, 75m, monthly, 3, ContributionState.Scheduled)]),
            new SavingsGroupResponse(SavingsTerm.Medium, []),
            new SavingsGroupResponse(SavingsTerm.Long, []),
            new SavingsGroupResponse(SavingsTerm.Past, [])
        };
        return new(date, 100m, 25m, 75m, monthly,
            new DateOnly(date.Year, date.Month, 1).AddMonths(1), CurrentHolidays, groups)
        { Summaries = Summaries(groups, monthly) };
    }

    private static SavingsSummaryResponse[] Summaries(SavingsGroupResponse[] groups, decimal monthly)
    {
        var summaries = groups.Select(x => new SavingsSummaryResponse(x.Type, x.Items.Sum(y => y.Item.TargetAmount),
            x.Items.Sum(y => y.Item.AllocatedAmount), x.Items.Sum(y => y.Item.DrawnDownAmount),
            x.Items.Sum(y => y.Item.AvailableAmount), x.Items.Sum(y => y.RequiredContribution)))
            .Append(new(null, Example.TargetAmount, Example.AllocatedAmount, Example.DrawnDownAmount,
                Example.AvailableAmount, monthly)).ToArray();
        return summaries;
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static BunitContext CreateContext(HttpMessageHandler handler, TimeProvider clock)
    {
        var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        return context;
    }

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
