using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Contracts;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class AccountPageScenarios
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Given_account_input_When_submitting_Then_success_refreshes_or_failure_preserves_input(bool fail)
    {
        using var context = new BunitContext();
        var handler = new AccountHandler(fail);
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<Accounts>();
        page.WaitForAssertion(() => Assert.Contains("No savings accounts yet", page.Markup));
        page.Find("#provider").Change("Example provider");
        page.Find("#account-name").Change("Example savings");
        page.Find("#initial-funds").Change("125.50");

        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(handler.Submitted);
            Assert.Equal(125.50m, handler.Submitted.TotalFunds);
            if (fail)
            {
                Assert.Contains("Reload it and retry", page.Find("[role=alert]").TextContent);
                Assert.Equal("Example savings", page.Find("#account-name").GetAttribute("value"));
            }
            else
            {
                Assert.Contains("Account saved", page.Markup);
                Assert.Contains("Example savings", page.Find("table").TextContent);
            }
        });
    }

    private sealed class AccountHandler(bool fail) : HttpMessageHandler
    {
        public CreateAccountRequest? Submitted { get; private set; }
        private readonly List<AccountResponse> _accounts = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(_accounts) };
            Submitted = await request.Content!.ReadFromJsonAsync<CreateAccountRequest>(cancellationToken);
            if (fail)
                return new(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { title = "Reload it and retry" },
                        mediaType: new("application/problem+json"))
                };
            var account = new AccountResponse(Guid.NewGuid(), Submitted!.ProviderName, Submitted.AccountName,
                Submitted.Type, Submitted.TotalFunds, "AAAAAAAAAAA=");
            _accounts.Add(account);
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(account) };
        }
    }
}
