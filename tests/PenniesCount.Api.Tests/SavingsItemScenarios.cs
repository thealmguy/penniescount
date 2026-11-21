using System.Net;
using System.Net.Http.Json;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsItemScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
{
    [Fact]
    public async Task Given_an_item_When_changing_allocations_Then_only_allocation_changes_and_stale_updates_conflict()
    {
        using var client = factory.CreateClient();
        var item = await CreateAsync(client, 25m);
        foreach (var amount in new[] { 100m, 0m })
        {
            var oldVersion = item.Version;
            using var response = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
                new UpdateAllocationRequest { AllocatedAmount = amount, Version = oldVersion });
            response.EnsureSuccessStatusCode();
            var updated = (await response.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
            Assert.Equal(item with { AllocatedAmount = amount, Version = updated.Version }, updated);
            item = updated;
            using var stale = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
                new UpdateAllocationRequest { AllocatedAmount = 50m, Version = oldVersion });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        var loaded = await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}");
        Assert.Equal(item, loaded);
        using var edit = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}", new { name = "Changed" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
    }

    [Fact]
    public async Task Given_no_account_funds_When_allocating_to_an_item_Then_it_is_allowed_up_to_the_item_target()
    {
        using var client = factory.CreateClient();
        var item = await CreateAsync(client, 100m);
        using var tooMuch = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
            new UpdateAllocationRequest { AllocatedAmount = 100.01m, Version = item.Version });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        var loaded = await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}");
        Assert.Equal(100m, loaded!.AllocatedAmount);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 101, 0)]
    [InlineData(100, -1, 0)]
    [InlineData(100, 0, -1)]
    public async Task Given_invalid_item_values_When_creating_Then_validation_rejects_them(decimal target, decimal allocation, int days)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Example goal", TargetAmount = target, AllocatedAmount = allocation,
            StartDate = new(2027, 1, 1), TargetDate = new DateOnly(2027, 1, 1).AddDays(days)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_missing_dates_and_unknown_type_When_creating_Then_the_request_is_invalid()
    {
        using var client = factory.CreateClient();
        using var dates = await client.PostAsJsonAsync("/api/savings-items",
            new CreateSavingsItemRequest { Name = "Example goal", TargetAmount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, dates.StatusCode);
        using var type = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Example goal", TargetAmount = 100m, Type = (SavingsTerm)99,
            StartDate = new(2027, 1, 1), TargetDate = new(2027, 2, 1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, type.StatusCode);
    }

    internal static async Task<SavingsItemResponse> CreateAsync(HttpClient client, decimal allocation)
    {
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Example goal", Type = SavingsTerm.Medium, TargetAmount = 100m,
            AllocatedAmount = allocation, StartDate = new(2027, 1, 1), TargetDate = new(2027, 4, 1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
    }
}
