using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using PenniesCount.Contracts;
using PenniesCount.Web.Components.Layout;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class NewPagesScenarios
{
    [Fact]
    public void Given_a_forecast_When_opening_future_look_Then_it_shows_24_months_and_an_accessible_table()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/Pages/FutureLook.razor.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ => Json(Forecast())))
        {
            BaseAddress = new("http://localhost/")
        }));

        var page = context.Render<FutureLook>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(24, page.FindAll("#forecast-values tbody tr").Count);
            Assert.Equal(4, page.FindAll("#forecast-values thead th").Count - 2);
            Assert.Equal("January 2027", page.Find("#forecast-values tbody tr th").TextContent);
            Assert.Contains("10.00", page.Find("#forecast-values tbody tr").TextContent);
        });
        Assert.Equal("Stacked bar chart of monthly savings funding needs. Exact values are available in the table below.",
            page.Find(".forecast-chart-container").GetAttribute("aria-label"));
        Assert.Contains("no feasible allocation dates", page.Markup);
        Assert.Contains("Future goal: £10.00", page.Markup);
    }

    [Fact]
    public void Given_a_forecast_API_error_When_opening_future_look_Then_it_does_not_show_a_successful_empty_chart()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ =>
            throw new HttpRequestException())) { BaseAddress = new("http://localhost/") }));

        var page = context.Render<FutureLook>();

        page.WaitForAssertion(() => Assert.Contains("API is unavailable", page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("#forecast-values"));
        Assert.Empty(page.FindAll("canvas"));
    }

    [Fact]
    public void Given_a_forecast_beyond_holiday_coverage_When_opened_Then_estimates_are_identified()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/Pages/FutureLook.razor.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TimeProvider.System);
        var forecast = Forecast() with { HolidayCalendar = new("GOV.UK", new(2027, 12, 31), null, false, null) };
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ => Json(forecast)))
        {
            BaseAddress = new("http://localhost/")
        }));

        var page = context.Render<FutureLook>();

        page.WaitForAssertion(() => Assert.Contains("Some future months lack bank-holiday coverage", page.Markup));
        Assert.Contains("weekdays only", page.Markup);
    }

    [Fact]
    public void Given_an_open_forecast_When_the_local_date_rolls_over_Then_the_forecast_is_refreshed()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 1, 31, 23, 59, 45, TimeSpan.Zero));
        var requests = 0;
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/Pages/FutureLook.razor.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ =>
        {
            requests++;
            return Json(Forecast(DateOnly.FromDateTime(clock.GetLocalNow().DateTime)));
        })) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<FutureLook>();
        page.WaitForAssertion(() => Assert.Equal("31 Jan 2027", page.Find("#forecast-date").TextContent));

        clock.Advance(TimeSpan.FromSeconds(30));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("01 Feb 2027", page.Find("#forecast-date").TextContent);
            Assert.Equal("February 2027", page.Find("#forecast-values tbody tr th").TextContent);
            Assert.Equal(2, requests);
        });
    }

    [Fact]
    public void Given_a_chart_failure_When_loading_forecast_Then_the_error_and_values_are_visible()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/Pages/FutureLook.razor.js");
        module.SetupVoid("render", _ => true).SetException(new JSException("Chart unavailable"));
        module.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ => Json(Forecast())))
        {
            BaseAddress = new("http://localhost/")
        }));

        var page = context.Render<FutureLook>();

        page.WaitForAssertion(() => Assert.Contains("interactive chart could not be loaded", page.Find("[role=alert]").TextContent));
        Assert.Equal(24, page.FindAll("#forecast-values tbody tr").Count);
    }

    [Fact]
    public void Given_left_menu_navigation_When_rendered_Then_all_requested_pages_are_linked()
    {
        using var context = new BunitContext();

        var page = context.Render<NavMenu>();

        Assert.Contains(page.FindAll("a").Select(x => x.GetAttribute("href")), href => href == "savings-items");
        Assert.Contains(page.FindAll("a").Select(x => x.GetAttribute("href")), href => href == "future-look");
        Assert.Contains(page.FindAll("a").Select(x => x.GetAttribute("href")), href => href == "about");
    }

    [Fact]
    public void Given_the_about_page_When_opened_Then_it_explains_funding_semantics_and_loads_holiday_status()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(_ => Json(
            new DashboardResponse(new(2027, 1, 15), 100m, 25m, 75m, 0m, new(2027, 2, 1),
                new("GOV.UK bank holidays (England and Wales)", new(2028, 12, 31), null, false, null), []))))
        {
            BaseAddress = new("http://localhost/")
        }));

        var page = context.Render<About>();

        page.WaitForAssertion(() => Assert.Contains("GOV.UK bank holidays", page.Find("#holiday-calendar-source").TextContent));
        Assert.Contains("including money already drawn down", page.Markup);
        Assert.Contains("the final payment may be smaller", page.Markup);
    }

    [Fact]
    public void Given_a_saved_allocation_When_viewing_savings_items_Then_the_list_refreshes()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(TimeProvider.System);
        var saved = false;
        var item = new SavingsItemResponse(Guid.NewGuid(), "Example goal", SavingsTerm.Short, 100m, 25m,
            new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=") { AvailableAmount = 25m };
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReplyHandler(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                saved = true;
                return Json(item with { AllocatedAmount = 100m, AvailableAmount = 100m });
            }
            var current = saved ? item with { AllocatedAmount = 100m, AvailableAmount = 100m } : item;
            var row = new DashboardItemResponse(current, saved ? 0m : 75m, saved ? 0m : 25m,
                saved ? 0 : 3, saved ? ContributionState.Funded : ContributionState.Scheduled);
            return Json(new DashboardResponse(new(2027, 1, 15), 100m, current.AvailableAmount,
                100m - current.AvailableAmount, row.RequiredContribution, new(2027, 2, 1),
                new("GOV.UK", new(2028, 12, 31), null, false, null),
                [new(SavingsTerm.Short, [row])]));
        })) { BaseAddress = new("http://localhost/") }));

        var page = context.Render<SavingsItems>();
        page.WaitForAssertion(() => Assert.Contains("25.00", page.Find("tbody").TextContent));
        page.FindAll("button").Single(x => x.TextContent == "Update allocation").Click();
        page.Find("#allocated-total").Change("100");
        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("form"));
            Assert.Contains("100.00", page.Find("tbody").TextContent);
            Assert.Contains("Fully allocated", page.Find("tbody").TextContent);
        });
    }

    private static FutureLookResponse Forecast(DateOnly? asOf = null)
    {
        var date = asOf ?? new DateOnly(2027, 1, 15);
        var months = Enumerable.Range(0, 24).Select(index =>
        {
            var month = new DateOnly(date.Year, date.Month, 1).AddMonths(index);
            var categories = Enum.GetValues<SavingsTerm>().Select(type => new ForecastCategoryResponse(type,
                index == 0 && type == SavingsTerm.Short ? 10m : 0m)).ToArray();
            return new ForecastMonthResponse(month, categories, categories.Sum(x => x.Amount));
        }).ToArray();
        return new(date,
            new("GOV.UK bank holidays (England and Wales)", new(2028, 12, 31), null, false, null),
            months, [new(Guid.NewGuid(), "Future goal", 10m)]);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
