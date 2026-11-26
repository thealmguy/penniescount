using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Contracts;
using PenniesCount.Web.Components;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class SavingsItemPageScenarios
{
    [Fact]
    public void Given_no_category_When_creating_Then_selection_is_required_before_an_API_write()
    {
        using var context = new BunitContext();
        var handler = new ItemHandler(false);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<CreateSavingsItem>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("form")));
        page.Find("#item-name").Change("Example");
        page.Find("#target-amount").Change("100");
        page.Find("form").Submit();
        Assert.Contains("Category is required", page.Find(".validation-errors").TextContent);
        Assert.Null(handler.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Given_legacy_details_When_editing_Then_category_is_required_and_save_errors_retain_input(bool fail)
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var handler = new ItemHandler(fail);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var notified = false;
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(ItemHandler.Example, 75m, 25m, 3, ContributionState.Scheduled)])])
            .Add(x => x.OnSaved, () => notified = true));
        Assert.Contains("Uncategorised", page.Markup);
        page.FindAll("button").Single(x => x.TextContent == "Edit details").Click();
        Assert.Equal("details-title", page.Find("dialog").GetAttribute("aria-labelledby"));
        Assert.Equal("Example goal", page.Find("#edit-item-name").GetAttribute("value"));
        Assert.Equal("2027-04-01", page.Find("#edit-target-date").GetAttribute("value"));
        page.Find("#edit-item-name").Change("Renamed");
        page.Find("#edit-target-date").Change("2028-04-01");
        page.Find("dialog form").Submit();
        Assert.Contains("Category is required", page.Find(".validation-errors").TextContent);
        Assert.Null(handler.Details);
        page.Find("#edit-item-category").Change("HolidaysAndTravel");
        page.Find("dialog form").Submit();
        page.WaitForAssertion(() =>
        {
            Assert.Equal("Renamed", handler.Details?.Name);
            Assert.Equal(new DateOnly(2028, 4, 1), handler.Details?.TargetDate);
            Assert.Equal(SavingsCategory.HolidaysAndTravel, handler.Details?.Category);
            Assert.Equal(ItemHandler.Example.Version, handler.Details?.Version);
            Assert.Null(handler.Allocation);
            Assert.Equal(!fail, notified);
            if (fail)
            {
                Assert.Contains("Reload and retry", page.Find("dialog [role=alert]").TextContent);
                Assert.Equal("Renamed", page.Find("#edit-item-name").GetAttribute("value"));
                Assert.Equal("2028-04-01", page.Find("#edit-target-date").GetAttribute("value"));
                Assert.Equal("HolidaysAndTravel", page.Find("#edit-item-category").GetAttribute("value"));
            }
            else Assert.Empty(page.FindAll("form"));
        });
        if (fail)
        {
            page.Find("dialog").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
            Assert.Empty(page.FindAll("form"));
            page.FindAll("button").Single(x => x.TextContent == "Edit details").Click();
            Assert.Empty(page.FindAll("[role=alert]"));
            Assert.Equal("Example goal", page.Find("#edit-item-name").GetAttribute("value"));
        }
    }

    [Fact]
    public async Task Given_categorised_details_When_saving_Then_current_category_is_prefilled_and_dismissal_is_locked()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new PendingHandler(reply.Task)) { BaseAddress = new("http://localhost/") }));
        var item = ItemHandler.Example with { Category = SavingsCategory.EmergencyFund };
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(item, 75m, 25m, 3, ContributionState.Scheduled)])]));
        page.FindAll("button").Single(x => x.TextContent == "Edit details").Click();
        Assert.Equal("EmergencyFund", page.Find("#edit-item-category").GetAttribute("value"));
        var submit = page.Find("dialog form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find("dialog fieldset").HasAttribute("disabled")));
        page.Find("dialog").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Single(page.FindAll("dialog form"));
        reply.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(item) });
        await submit;
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void Given_a_missing_deadline_When_editing_details_Then_validation_prevents_a_write()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var handler = new ItemHandler(false);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var item = ItemHandler.Example with { Category = SavingsCategory.Other };
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(item, 75m, 25m, 3, ContributionState.Scheduled)])]));
        page.FindAll("button").Single(x => x.TextContent == "Edit details").Click();
        page.Find("#edit-target-date").Change("0001-01-01");
        page.Find("dialog form").Submit();
        Assert.Contains("Target date is required", page.Find(".validation-errors").TextContent);
        Assert.Contains("invalid", page.Find("#edit-target-date").ClassList);
        Assert.Null(handler.Details);
    }

    [Fact]
    public void Given_today_from_the_api_When_creating_an_item_Then_dates_are_defaulted_but_amendable()
    {
        using var context = new BunitContext();
        var handler = new ItemHandler(false);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<CreateSavingsItem>();
        page.WaitForAssertion(() => Assert.Equal("2027-01-15", page.Find("#start-date").GetAttribute("value")));
        page.Find("#item-name").Change("Example goal");
        Assert.Equal(new[]
        {
            "Select a category", "Emergency Fund", "House & Garden", "Transport", "Holidays & Travel",
            "Children & Education", "Gifts & Christmas", "Leisure & Technology", "Long-Term Wealth Building", "Other"
        }, page.FindAll("#item-category option").Select(x => x.TextContent));
        page.Find("#item-category").Change("Transport");
        Assert.Contains("Savings terms follow the Save by date automatically", page.Markup);
        page.Find("#target-amount").Change("100");
        page.Find("#initial-allocation").Change("25");
        page.Find("#start-date").Change("2027-01-01");
        page.Find("#target-date").Change("2027-04-01");

        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(handler.Created);
            Assert.Equal(new DateOnly(2027, 1, 1), handler.Created.StartDate);
            Assert.Equal(new DateOnly(2027, 4, 1), handler.Created.TargetDate);
            Assert.Equal(25m, handler.Created.AllocatedAmount);
            Assert.Equal(SavingsCategory.Transport, handler.Created.Category);
            Assert.Equal("http://localhost/savings-items", context.Services.GetRequiredService<NavigationManager>().Uri);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Given_a_selected_past_item_When_saving_allocation_Then_only_allocation_is_submitted_and_failures_are_visible(bool fail)
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var handler = new ItemHandler(fail);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var notified = false;
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Past,
                [new(ItemHandler.Example with { Type = SavingsTerm.Past }, 75m, 75m, 0, ContributionState.NeededNow)])])
            .Add(x => x.OnSaved, () => notified = true));
        Assert.Equal("Historic", page.Find("h2").TextContent);
        Assert.Single(page.FindAll("section[aria-label='Historic']"));
        Assert.Contains("Needed now", page.Markup);
        page.Find("button").Click();
        var modal = page.Find("dialog");
        Assert.Equal("allocation-title", modal.GetAttribute("aria-labelledby"));
        Assert.Single(modal.QuerySelectorAll("input"));
        Assert.Contains("Example goal", modal.TextContent);
        page.Find("#allocated-total").Change("50");

        page.Find("dialog form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(50m, handler.Allocation?.AllocatedAmount);
            Assert.Equal(ItemHandler.Example.Version, handler.Allocation?.Version);
            Assert.Equal(!fail, notified);
            if (fail)
            {
                Assert.Contains("Reload and retry", page.Find("dialog [role=alert]").TextContent);
                Assert.Single(page.FindAll("[role=alert]"));
                Assert.Equal("50", page.Find("#allocated-total").GetAttribute("value"));
            }
            else Assert.Empty(page.FindAll("form"));
        });
    }

    [Fact]
    public void Given_a_failed_allocation_edit_When_cancelled_and_reopened_Then_the_modal_resets_errors_and_unsaved_changes()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var handler = new ItemHandler(true);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(ItemHandler.Example, 75m, 25m, 3, ContributionState.Scheduled)])]));
        page.Find("button").Click();
        page.Find("#allocated-total").Change("50");
        page.Find("dialog form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("dialog [role=alert]")));

        page.FindAll("dialog button").Single(x => x.TextContent == "Cancel").Click();

        Assert.Empty(page.FindAll("form"));
        page.Find("button").Click();
        Assert.Empty(page.FindAll("[role=alert]"));
        Assert.Equal("25", page.Find("#allocated-total").GetAttribute("value"));

        page.Find("dialog").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public async Task Given_an_in_flight_allocation_save_When_escape_is_pressed_Then_the_modal_stays_open_until_the_save_completes()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new PendingHandler(reply.Task))
        {
            BaseAddress = new("http://localhost/")
        }));
        var notified = false;
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(ItemHandler.Example, 75m, 25m, 3, ContributionState.Scheduled)])])
            .Add(x => x.OnSaved, () => notified = true));
        page.Find("button").Click();
        page.Find("#allocated-total").Change("50");

        var submit = page.Find("dialog form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find("dialog fieldset").HasAttribute("disabled")));
        page.Find("dialog").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Single(page.FindAll("dialog form"));
        Assert.False(notified);
        reply.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(ItemHandler.Example with { AllocatedAmount = 50m }) });
        await submit;
        Assert.Empty(page.FindAll("form"));
        Assert.True(notified);
    }

    [Fact]
    public void Given_an_invalid_allocation_When_submitted_Then_validation_stays_in_the_modal_without_an_api_write()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/SavingsItemsList.razor.js").Mode = JSRuntimeMode.Loose;
        var handler = new ItemHandler(false);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Short, [new(ItemHandler.Example, 75m, 25m, 3, ContributionState.Scheduled)])]));
        page.Find("button").Click();
        page.Find("#allocated-total").Change("-1");

        page.Find("dialog form").Submit();

        Assert.NotEmpty(page.Find("dialog .validation-errors").TextContent);
        Assert.Null(handler.Allocation);
        Assert.Single(page.FindAll("dialog form"));
    }

    private sealed class PendingHandler(Task<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply;
    }

    private sealed class ItemHandler(bool fail) : HttpMessageHandler
    {
        public static SavingsItemResponse Example { get; } = new(Guid.NewGuid(), "Example goal", SavingsTerm.Short,
            100m, 25m, new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=") { AvailableAmount = 25m };
        public CreateSavingsItemRequest? Created { get; private set; }
        public UpdateAllocationRequest? Allocation { get; private set; }
        public UpdateSavingsItemDetailsRequest? Details { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new SavingsItemsResponse(new(2027, 1, 15), [])) };
            if (request.Method == HttpMethod.Post)
            {
                Created = await request.Content!.ReadFromJsonAsync<CreateSavingsItemRequest>(ct);
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(Example) };
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/details"))
                Details = await request.Content!.ReadFromJsonAsync<UpdateSavingsItemDetailsRequest>(ct);
            else
                Allocation = await request.Content!.ReadFromJsonAsync<UpdateAllocationRequest>(ct);
            return fail
                ? new(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { title = "Reload and retry" }, mediaType: new("application/problem+json"))
                }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(Example with { AllocatedAmount = 50m }) };
        }
    }
}
