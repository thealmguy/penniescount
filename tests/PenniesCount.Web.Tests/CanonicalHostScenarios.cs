using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PenniesCount.Web.Tests;

public sealed class CanonicalHostScenarios
{
    [Theory]
    [InlineData("https://www.penniescount.example/")]
    [InlineData("http://www.penniescount.example/")]
    [InlineData("https://default-host.example.net/")]
    public async Task Given_a_canonical_host_When_browsing_another_host_Then_it_permanently_redirects_over_https(
        string alias)
    {
        using var factory = CreateFactory("penniescount.example");
        using var client = CreateClient(factory, alias);

        using var response = await client.GetAsync("/accounts/3?view=all&x=%20y");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://penniescount.example/accounts/3?view=all&x=%20y", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("https://penniescount.example/")]
    [InlineData("https://PennIesCount.Example/")]
    public async Task Given_a_canonical_host_When_browsing_it_Then_the_request_is_served(string address)
    {
        using var factory = CreateFactory("penniescount.example");
        using var client = CreateClient(factory, address);

        using var response = await client.GetAsync("/missing-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Given_no_canonical_host_When_browsing_any_host_Then_the_request_is_served(string? canonicalHost)
    {
        using var factory = CreateFactory(canonicalHost);
        using var client = CreateClient(factory, "https://www.penniescount.example/");

        using var response = await client.GetAsync("/missing-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string? canonicalHost) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AllowedHosts", "*");
            if (canonicalHost is not null)
                builder.UseSetting("CanonicalHost", canonicalHost);
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string address) =>
        factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new(address) });
}
