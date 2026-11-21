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
    public void Given_today_from_the_api_When_creating_an_item_Then_dates_are_defaulted_but_amendable()
    {
        using var context = new BunitContext();
        var handler = new ItemHandler(false);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<CreateSavingsItem>();
        page.WaitForAssertion(() => Assert.Equal("2027-01-15", page.Find("#start-date").GetAttribute("value")));
        page.Find("#item-name").Change("Example goal");
        Assert.Empty(page.FindAll("select"));
        Assert.Contains("Categories follow the Save by date automatically", page.Markup);
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
            Assert.Equal("http://localhost/", context.Services.GetRequiredService<NavigationManager>().Uri);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Given_a_selected_past_item_When_saving_allocation_Then_only_allocation_is_submitted_and_failures_are_visible(bool fail)
    {
        using var context = new BunitContext();
        var handler = new ItemHandler(fail);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var notified = false;
        var page = context.Render<SavingsItemsList>(parameters => parameters
            .Add(x => x.Groups, [new(SavingsTerm.Past,
                [new(ItemHandler.Example with { Type = SavingsTerm.Past }, 75m, 75m, 0, ContributionState.NeededNow)])])
            .Add(x => x.OnSaved, () => notified = true));
        Assert.Equal("Past savings goals", page.Find("h2").TextContent);
        Assert.Single(page.FindAll("section[aria-label='Past savings goals']"));
        Assert.Contains("Needed now", page.Markup);
        page.Find("button").Click();
        Assert.Single(page.FindAll("input"));
        page.Find("#allocated-total").Change("50");

        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(50m, handler.Allocation?.AllocatedAmount);
            Assert.Equal(ItemHandler.Example.Version, handler.Allocation?.Version);
            Assert.Equal(!fail, notified);
            if (fail)
            {
                Assert.Contains("Reload and retry", page.Find("[role=alert]").TextContent);
                Assert.Equal("50", page.Find("#allocated-total").GetAttribute("value"));
            }
            else Assert.Empty(page.FindAll("form"));
        });
    }

    private sealed class ItemHandler(bool fail) : HttpMessageHandler
    {
        public static SavingsItemResponse Example { get; } = new(Guid.NewGuid(), "Example goal", SavingsTerm.Short,
            100m, 25m, new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=");
        public CreateSavingsItemRequest? Created { get; private set; }
        public UpdateAllocationRequest? Allocation { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new SavingsItemsResponse(new(2027, 1, 15), [])) };
            if (request.Method == HttpMethod.Post)
            {
                Created = await request.Content!.ReadFromJsonAsync<CreateSavingsItemRequest>(ct);
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(Example) };
            }
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
