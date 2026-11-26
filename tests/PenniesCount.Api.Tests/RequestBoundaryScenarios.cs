using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class RequestBoundaryScenarios
{
    [Theory]
    [InlineData("POST", "/drawdowns")]
    [InlineData("PUT", "/drawdowns/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/drawdowns/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/removal")]
    [InlineData("PUT", "/details")]
    public async Task Given_non_json_drawdown_writes_When_submitted_Then_they_are_rejected_before_database_access(string method, string suffix)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method),
            "/api/savings-items/00000000-0000-0000-0000-000000000001" + suffix)
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain")
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Given_a_non_json_submission_When_creating_an_account_Then_it_is_rejected_before_database_access()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var json = JsonSerializer.Serialize(new CreateAccountRequest
        {
            ProviderName = "Example provider", AccountName = "Example account", TotalFunds = 100m
        });

        using var response = await client.PostAsync("/api/accounts", new StringContent(json, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Given_an_unrecognised_host_When_requesting_the_api_Then_host_filtering_rejects_it()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = "unrecognised.example";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_malformed_json_When_submitting_Then_it_returns_explicit_problem_details()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/accounts",
            new StringContent("{", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
