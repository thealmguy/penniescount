using System.Net;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class ApiAuthenticationScenarios
{
    [Fact]
    public async Task Given_a_signed_in_user_When_calling_the_api_Then_the_user_token_is_sent()
    {
        using var client = new BudgetClient(new HttpClient(new ReplyHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("user-token", request.Headers.Authorization?.Parameter);
        })) { BaseAddress = new("https://localhost/") }, new Tokens());

        await client.GetHealthAsync();
    }

    [Fact]
    public async Task Given_a_token_failure_When_calling_the_api_Then_no_unauthenticated_request_is_sent()
    {
        var sent = false;
        using var client = new BudgetClient(new HttpClient(new ReplyHandler(_ => sent = true))
            { BaseAddress = new("https://localhost/") }, new Tokens(fail: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetHealthAsync());
        Assert.False(sent);
    }

    private sealed class Tokens(bool fail = false) : IApiAccessTokenProvider
    {
        public Task<string> GetTokenAsync() => fail
            ? throw new InvalidOperationException("Token acquisition failed.")
            : Task.FromResult("user-token");
    }

    private sealed class ReplyHandler(Action<HttpRequestMessage> inspect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            inspect(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"Ready"}""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
