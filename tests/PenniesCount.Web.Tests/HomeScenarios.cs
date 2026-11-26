using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
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
    public async Task Given_drawdowns_When_opening_home_Then_category_totals_and_chart_use_available_not_funded_allocation()
    {
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(15m))), TimeProvider.System);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "render"));
        Assert.Equal("Savings term", page.Find("#savings-totals thead th").TextContent);
        Assert.Equal(page.FindAll("#savings-totals thead th").Skip(1).Select(x => x.TextContent),
            page.FindAll("#purpose-savings-totals thead th").Skip(1).Select(x => x.TextContent));
        Assert.Equal(11, page.FindAll("#purpose-savings-totals tbody tr").Count);
        Assert.Equal(new[] { "Transport", "100.00", "25.00", "10.00", "15.00", "25.00" },
            page.FindAll("#purpose-savings-totals tr[data-category='Transport'] > *")
                .Select(x => x.TextContent.Replace("£", "")));
        Assert.Contains("Uncategorised", page.Find("#purpose-savings-totals").TextContent);
        Assert.Contains("15.00", page.Find("#purpose-savings-totals tr[data-category='GrandTotal']").TextContent);
        var call = module.Invocations.Single(x => x.Identifier == "render");
        Assert.Equal(15d, Assert.IsType<double[]>(call.Arguments[2]).Sum());
        Assert.Equal("Transport", Assert.IsType<string[]>(call.Arguments[1])[2]);
        Assert.Equal("£15.00", Assert.IsType<string[]>(call.Arguments[3])[2]);
        Assert.Contains("Available to spend", page.Find("#category-allocation-chart").GetAttribute("aria-label"));

        await context.DisposeAsync();
        Assert.Single(module.Invocations, x => x.Identifier == "dispose");
    }

    [Fact]
    public void Given_no_available_allocations_When_opening_home_Then_it_keeps_category_rows_without_an_empty_chart()
    {
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(0m))), TimeProvider.System);
        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("No available allocations to display", page.Markup));
        Assert.Equal(11, page.FindAll("#purpose-savings-totals tbody tr").Count);
        Assert.Empty(page.FindAll("canvas"));
    }

    [Fact]
    public void Given_changed_allocations_When_refreshing_Then_the_chart_matches_the_latest_category_table()
    {
        var amount = 15m;
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(amount))), TimeProvider.System);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "render"));
        amount = 20m;

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(x => x.Identifier == "render")));
        var latest = module.Invocations.Last(x => x.Identifier == "render");
        Assert.Equal(20d, Assert.IsType<double[]>(latest.Arguments[2]).Sum());
        Assert.Contains("20.00", page.Find("#purpose-savings-totals tr[data-category='Transport']").TextContent);
    }

    [Fact]
    public void Given_a_chart_failure_When_opening_home_Then_the_error_and_complete_table_remain_visible()
    {
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(15m))), TimeProvider.System);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.SetupVoid("render", _ => true).SetException(new JSException("Chart unavailable"));
        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("chart could not be loaded", page.Find("[role=alert]").TextContent));
        Assert.Equal(11, page.FindAll("#purpose-savings-totals tbody tr").Count);
        Assert.Contains("15.00", page.Find("#total-allocated").TextContent);
    }

    [Fact]
    public void Given_a_refresh_during_chart_rendering_When_it_finishes_Then_the_latest_budget_is_rendered()
    {
        var amount = 15m;
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(amount))), TimeProvider.System);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var pending = module.SetupVoid("render", _ => true);
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "render"));
        amount = 20m;
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("20.00", page.Find("#total-allocated").TextContent));

        pending.SetVoidResult();

        page.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(x => x.Identifier == "render")));
        var latest = module.Invocations.Last(x => x.Identifier == "render");
        Assert.Equal(20d, Assert.IsType<double[]>(latest.Arguments[2]).Sum());
        Assert.Equal("£20.00", latest.Arguments[4]);
    }

    [Fact]
    public void Given_allocations_becoming_empty_then_funded_When_refreshing_Then_the_chart_is_disposed_and_recreated()
    {
        var amount = 15m;
        using var context = CreateContext(new ReplyHandler(_ => Json(CategoryBudget(amount))), TimeProvider.System);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "render"));
        amount = 0m;
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "dispose"));
        Assert.Empty(page.FindAll("canvas"));
        amount = 10m;
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(x => x.Identifier == "render")));
        Assert.Single(page.FindAll("canvas"));
    }

    [Fact]
    public void Given_a_new_day_then_an_api_failure_When_refreshing_Then_chart_and_table_retain_explicitly_stale_values()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 1, 15, 23, 59, 45, TimeSpan.Zero));
        var fail = false;
        using var context = CreateContext(new ReplyHandler(_ => fail ? throw new HttpRequestException()
            : Json(CategoryBudget(clock.GetLocalNow().Day == 15 ? 15m : 20m) with
                { AsOfDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime) })), clock);
        var module = context.JSInterop.SetupModule("./Components/Pages/Home.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "render"));

        clock.Advance(TimeSpan.FromSeconds(30));

        page.WaitForAssertion(() => Assert.Equal(2, module.Invocations.Count(x => x.Identifier == "render")));
        Assert.Equal(20d, Assert.IsType<double[]>(module.Invocations.Last(x => x.Identifier == "render").Arguments[2]).Sum());
        fail = true;
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("last successful refresh", page.Markup));
        Assert.Contains("20.00", page.Find("#purpose-savings-totals tr[data-category='Transport']").TextContent);
        Assert.Equal(2, module.Invocations.Count(x => x.Identifier == "render"));
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
        { Summaries = Summaries(groups, monthly), PurposeSummaries = PurposeSummaries(groups) };
    }

    private static DashboardResponse CategoryBudget(decimal available)
    {
        var budget = Budget(new(2027, 1, 15), 25m);
        var groups = new[] { new SavingsGroupResponse(SavingsTerm.Short,
            [new(Example with { Category = SavingsCategory.Transport, DrawnDownAmount = 25m - available,
                AvailableAmount = available }, 75m, 25m, 3, ContributionState.Scheduled)]) };
        return budget with { TotalAllocated = available, Groups = groups,
            Summaries = [new(SavingsTerm.Short, 100m, 25m, 25m - available, available, 25m),
                new(null, 100m, 25m, 25m - available, available, 25m)],
            PurposeSummaries = PurposeSummaries(groups) };
    }

    private static SavingsPurposeSummaryResponse[] PurposeSummaries(SavingsGroupResponse[] groups) =>
        Enum.GetValues<SavingsCategory>().Select(x => (SavingsCategory?)x).Append(null).Select(category =>
        {
            var items = groups.SelectMany(x => x.Items).Where(x => x.Item.Category == category).ToArray();
            return new SavingsPurposeSummaryResponse(category, items.Sum(x => x.Item.TargetAmount),
                items.Sum(x => x.Item.AllocatedAmount), items.Sum(x => x.Item.DrawnDownAmount),
                items.Sum(x => x.Item.AvailableAmount), items.All(x => x.RequiredContribution.HasValue)
                    ? items.Sum(x => x.RequiredContribution) : null);
        }).ToArray();

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
        context.JSInterop.SetupModule("./Components/Pages/Home.razor.js").Mode = JSRuntimeMode.Loose;
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
