using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class HealthScenarios
{
    [Fact]
    public async Task Given_a_running_api_When_checking_health_Then_it_returns_a_typed_ready_response()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<HealthResponse>("/api/health");

        Assert.Equal("Ready", response?.Status);
    }
}
