using System.Net;
using System.Net.Http.Json;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class AccountScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
{
    [Fact]
    public async Task Given_an_account_When_replacing_balances_Then_every_submission_has_an_ordered_utc_snapshot()
    {
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow;
        var account = await CreateAsync(client, 100m);
        using var get = await client.GetAsync($"/api/accounts/{account.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        foreach (var amount in new[] { 100m, 200m, 50m })
        {
            using var update = await client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
                new UpdateBalanceRequest { TotalFunds = amount, Version = account.Version });
            update.EnsureSuccessStatusCode();
            account = (await update.Content.ReadFromJsonAsync<AccountResponse>())!;
        }

        var history = (await client.GetFromJsonAsync<BalanceSnapshotResponse[]>($"/api/accounts/{account.Id}/balance-history"))!;
        Assert.Equal(new[] { 100m, 100m, 200m, 50m }, history.Select(x => x.TotalFunds));
        Assert.All(history, snapshot =>
        {
            Assert.Equal(TimeSpan.Zero, snapshot.RecordedAtUtc.Offset);
            Assert.InRange(snapshot.RecordedAtUtc, before, DateTimeOffset.UtcNow);
        });
        Assert.Equal(history.OrderBy(x => x.Id), history);
        var reloaded = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}");
        Assert.Equal(50m, reloaded!.TotalFunds);
        Assert.Equal(account.Version, reloaded.Version);
    }

    [Fact]
    public async Task Given_a_stale_balance_When_saving_Then_it_conflicts_without_writing_history()
    {
        using var client = factory.CreateClient();
        var account = await CreateAsync(client, 10m);
        using var winner = await client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
            new UpdateBalanceRequest { TotalFunds = 20m, Version = account.Version });
        winner.EnsureSuccessStatusCode();
        using var stale = await client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
            new UpdateBalanceRequest { TotalFunds = 30m, Version = account.Version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var history = await client.GetFromJsonAsync<BalanceSnapshotResponse[]>($"/api/accounts/{account.Id}/balance-history");
        Assert.Equal(2, history!.Length);
        Assert.Equal(20m, history[^1].TotalFunds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10000000000000000L)]
    public async Task Given_invalid_money_When_saving_Then_validation_rejects_it_without_a_snapshot(decimal amount)
    {
        using var client = factory.CreateClient();
        var account = await CreateAsync(client, 0m);
        using var response = await client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
            new UpdateBalanceRequest { TotalFunds = amount, Version = account.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var history = await client.GetFromJsonAsync<BalanceSnapshotResponse[]>($"/api/accounts/{account.Id}/balance-history");
        Assert.Single(history!);
    }

    [Fact]
    public async Task Given_invalid_account_fields_When_creating_Then_problem_details_explain_validation()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest { Type = (AccountType)99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("ProviderName", json);
        Assert.Contains("AccountName", json);
        Assert.Contains("Type", json);
    }

    [Fact]
    public async Task Given_an_unknown_account_When_updating_Then_it_returns_not_found()
    {
        using var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/accounts/{Guid.NewGuid()}/balance",
            new UpdateBalanceRequest { TotalFunds = 1m, Version = "AAAAAAAAAAA=" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static async Task<AccountResponse> CreateAsync(HttpClient client, decimal amount)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest
        {
            ProviderName = "Example provider", AccountName = "Example savings",
            Type = AccountType.CashIsa, TotalFunds = amount
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    [Fact]
    public async Task Given_the_maximum_supported_balance_When_creating_Then_decimal_precision_is_preserved()
    {
        using var client = factory.CreateClient();
        var account = await CreateAsync(client, MoneyAttribute.Maximum);

        var loaded = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}");

        Assert.Equal(MoneyAttribute.Maximum, loaded!.TotalFunds);
    }

    [Fact]
    public async Task Given_two_updates_with_the_same_version_When_they_race_Then_one_wins_and_only_its_snapshot_is_written()
    {
        using var client = factory.CreateClient();
        var account = await CreateAsync(client, 5m);

        var responses = await Task.WhenAll(new[] { 10m, 20m }.Select(amount =>
            client.PutAsJsonAsync($"/api/accounts/{account.Id}/balance",
                new UpdateBalanceRequest { TotalFunds = amount, Version = account.Version })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var winner = (await responses.Single(response => response.IsSuccessStatusCode)
                .Content.ReadFromJsonAsync<AccountResponse>())!;
            var history = (await client.GetFromJsonAsync<BalanceSnapshotResponse[]>($"/api/accounts/{account.Id}/balance-history"))!;
            Assert.Equal(2, history.Length);
            Assert.Equal(winner.TotalFunds, history[^1].TotalFunds);
            var reloaded = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}");
            Assert.Equal(winner, reloaded);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }
}
