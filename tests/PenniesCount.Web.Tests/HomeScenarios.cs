using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Contracts;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class HomeScenarios
{
    [Fact]
    public void Given_no_savings_items_When_opening_home_Then_it_shows_the_empty_budget()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(new ReadyHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        var page = context.Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("No savings items yet", page.Markup));
    }

    private sealed class ReadyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SavingsItemsResponse(new(2027, 1, 15), []))
            });
    }
}
