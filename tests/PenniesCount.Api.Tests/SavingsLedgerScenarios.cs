using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsLedgerScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>, IAsyncLifetime
{
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Given_a_part_funded_item_When_recording_payments_Then_each_is_a_ledger_entry_that_raises_funding()
    {
        using var client = CreateClient();
        var item = await Create(client, 200m);
        var ledger = await Ledger(client, item);
        Assert.Equal(Today, ledger.AsOfDate);
        var opening = Assert.Single(ledger.Entries);
        Assert.Equal(new LedgerEntryResponse(opening.Id, LedgerEntryType.OpeningFunding, 200m, Today,
            "Initial allocation", Now, false, null, null, false), opening);

        using var paid = await Pay(client, item, 300m, Today.AddDays(-14));
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var payment = (await paid.Content.ReadFromJsonAsync<LedgerEntryResponse>())!;
        Assert.Equal(new LedgerEntryResponse(payment.Id, LedgerEntryType.AllocationPayment, 300m, Today.AddDays(-14),
            null, Now, false, null, null, false), payment);
        Assert.Equal($"/api/savings-items/{item.Id}/ledger/{payment.Id}", paid.Headers.Location!.OriginalString);
        Assert.Equal(payment, await client.GetFromJsonAsync<LedgerEntryResponse>(paid.Headers.Location));

        item = await Item(client, item);
        Assert.Equal(500m, item.AllocatedAmount);
        using var excess = await Pay(client, item, 500.01m, Today);
        Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        Assert.Contains("Payments cannot take total funding above the target.", await excess.Content.ReadAsStringAsync());
        using var full = await Pay(client, item, 500m, Today);
        Assert.Equal(HttpStatusCode.Created, full.StatusCode);
        item = await Item(client, item);
        Assert.Equal(1000m, item.AllocatedAmount);
        ledger = await Ledger(client, item);
        Assert.Equal(payment.Id, ledger.Entries[0].Id);
        Assert.Equal(ledger.Entries.OrderBy(x => x.EffectiveDate).ThenBy(x => x.RecordedAtUtc).ThenBy(x => x.Id),
            ledger.Entries);
        Assert.Equal(3, ledger.Entries.Length);
        Assert.Equal(item, ledger.Item);
    }

    [Theory]
    [InlineData(0, 0, "Amount")]
    [InlineData(-1, 0, "Amount")]
    [InlineData(0.001, 0, "Amount")]
    [InlineData(1, 1, "Payment date is required and cannot be in the future.")]
    [InlineData(1, -1, "Payment date is required and cannot be in the future.")]
    public async Task Given_invalid_payments_When_submitted_Then_nothing_is_recorded(decimal amount, int days, string error)
    {
        using var client = CreateClient();
        var item = await Create(client, 200m);
        var paidOn = days < 0 ? DateOnly.MinValue : Today.AddDays(days);
        using var response = await Pay(client, item, amount, paidOn);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, await response.Content.ReadAsStringAsync());
        Assert.Single((await Ledger(client, item)).Entries);
        Assert.Equal(item, await Item(client, item));
    }

    [Fact]
    public async Task Given_missing_stale_or_removed_items_When_paying_or_reversing_Then_writes_are_rejected()
    {
        using var client = CreateClient();
        var item = await Create(client, 200m);
        var opening = (await Ledger(client, item)).Entries.Single();
        using var badVersion = await client.PostAsJsonAsync($"/api/savings-items/{Guid.NewGuid()}/payments",
            new { amount = 1m, paidOn = Today, version = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);
        using var badReversal = await Reverse(client, Guid.NewGuid(), Guid.NewGuid(), "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, badReversal.StatusCode);
        using var missingPayment = await client.PostAsJsonAsync($"/api/savings-items/{Guid.NewGuid()}/payments",
            new { amount = 1m, paidOn = Today, version = item.Version });
        Assert.Equal(HttpStatusCode.NotFound, missingPayment.StatusCode);
        using var missingItem = await Reverse(client, Guid.NewGuid(), opening.Id, item.Version);
        Assert.Equal(HttpStatusCode.NotFound, missingItem.StatusCode);
        using var missingEntry = await Reverse(client, item.Id, Guid.NewGuid(), item.Version);
        Assert.Equal(HttpStatusCode.NotFound, missingEntry.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/savings-items/{Guid.NewGuid()}/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/savings-items/{item.Id}/ledger/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/savings-items/{Guid.NewGuid()}/ledger/{opening.Id}")).StatusCode);

        using var paid = await Pay(client, item, 1m, Today);
        paid.EnsureSuccessStatusCode();
        using var stalePayment = await Pay(client, item, 1m, Today);
        Assert.Equal(HttpStatusCode.Conflict, stalePayment.StatusCode);
        using var staleReversal = await Reverse(client, item.Id, opening.Id, item.Version);
        Assert.Equal(HttpStatusCode.Conflict, staleReversal.StatusCode);

        item = await Item(client, item);
        using var removal = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/removal", new { version = item.Version });
        removal.EnsureSuccessStatusCode();
        item = await Item(client, item);
        using var removedPayment = await Pay(client, item, 1m, Today);
        Assert.Equal(HttpStatusCode.Conflict, removedPayment.StatusCode);
        Assert.Contains("Removed items are read-only.", await removedPayment.Content.ReadAsStringAsync());
        using var removedReversal = await Reverse(client, item.Id, opening.Id, item.Version);
        Assert.Equal(HttpStatusCode.Conflict, removedReversal.StatusCode);
        Assert.Equal(2, (await Ledger(client, item)).Entries.Length);
    }

    [Fact]
    public async Task Given_ledger_entries_When_reversing_Then_an_offsetting_entry_is_appended_once()
    {
        using var client = CreateClient();
        var item = await Create(client, 200m);
        var paidOn = Today.AddDays(-3);
        using var paid = await Pay(client, item, 300m, paidOn);
        var payment = (await paid.Content.ReadFromJsonAsync<LedgerEntryResponse>())!;
        item = await Item(client, item);

        using var reversed = await Reverse(client, item.Id, payment.Id, item.Version);
        Assert.Equal(HttpStatusCode.Created, reversed.StatusCode);
        var reversal = (await reversed.Content.ReadFromJsonAsync<LedgerEntryResponse>())!;
        Assert.Equal(new LedgerEntryResponse(reversal.Id, LedgerEntryType.Reversal, 300m, paidOn, null, Now, false,
            payment.Id, null, false), reversal);
        Assert.Equal($"/api/savings-items/{item.Id}/ledger/{reversal.Id}", reversed.Headers.Location!.OriginalString);
        Assert.True((await client.GetFromJsonAsync<LedgerEntryResponse>(
            $"/api/savings-items/{item.Id}/ledger/{payment.Id}"))!.IsReversed);
        item = await Item(client, item);
        Assert.Equal(200m, item.AllocatedAmount);

        using var again = await Reverse(client, item.Id, payment.Id, item.Version);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already been reversed", await again.Content.ReadAsStringAsync());
        using var ofReversal = await Reverse(client, item.Id, reversal.Id, item.Version);
        Assert.Equal(HttpStatusCode.BadRequest, ofReversal.StatusCode);
        var problem = (await ofReversal.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        Assert.Equal(["Reversal entries cannot be reversed."], problem.Errors["EntryId"]);
        Assert.Equal(item, await Item(client, item));
        Assert.Equal(3, (await Ledger(client, item)).Entries.Length);
    }

    [Fact]
    public async Task Given_drawdowns_and_adjustments_When_reversing_Then_funding_must_stay_between_drawdowns_and_target()
    {
        using var client = CreateClient();
        var item = await Create(client, 600m);
        var opening = (await Ledger(client, item)).Entries.Single();
        using var spent = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/drawdowns",
            new { amount = 500m, spentOn = Today, version = item.Version });
        var drawdown = (await spent.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!;
        item = await Item(client, item);
        using var belowDrawdowns = await Reverse(client, item.Id, opening.Id, item.Version);
        Assert.Equal(HttpStatusCode.BadRequest, belowDrawdowns.StatusCode);
        var fundingProblem = (await belowDrawdowns.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
        Assert.Equal(["Reversing this entry would take total funding below recorded drawdowns or above the target."],
            fundingProblem.Errors["EntryId"]);

        using var drawdownReversal = await Reverse(client, item.Id, drawdown.Id, item.Version);
        Assert.Equal(HttpStatusCode.Created, drawdownReversal.StatusCode);
        item = await Item(client, item);
        Assert.Equal((600m, 0m), (item.AllocatedAmount, item.DrawnDownAmount));

        using var lowered = await Allocate(client, item, 100m);
        lowered.EnsureSuccessStatusCode();
        item = await Item(client, item);
        using var raised = await Allocate(client, item, 1000m);
        raised.EnsureSuccessStatusCode();
        item = await Item(client, item);
        var adjustments = (await Ledger(client, item)).Entries.Where(x => x.Type == LedgerEntryType.FundingAdjustment)
            .OrderBy(x => x.Amount).ToArray();
        Assert.Equal([-500m, 900m], adjustments.Select(x => x.Amount));
        Assert.All(adjustments, x => Assert.Equal((Today, "Total funding updated", Now, false),
            (x.EffectiveDate, x.Description, x.RecordedAtUtc, x.IsInferred)));
        using var aboveTarget = await Reverse(client, item.Id, adjustments[0].Id, item.Version);
        Assert.Equal(HttpStatusCode.BadRequest, aboveTarget.StatusCode);
        using var spentAgain = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/drawdowns",
            new { amount = 100m, spentOn = Today, version = item.Version });
        spentAgain.EnsureSuccessStatusCode();
        item = await Item(client, item);
        using var exactlyDrawdowns = await Reverse(client, item.Id, adjustments[1].Id, item.Version);
        Assert.Equal(HttpStatusCode.Created, exactlyDrawdowns.StatusCode);
        item = await Item(client, item);
        Assert.Equal((100m, 0m), (item.AllocatedAmount, item.AvailableAmount));
        using var topUp = await Pay(client, item, 400m, Today);
        topUp.EnsureSuccessStatusCode();
        item = await Item(client, item);
        using var exactlyTarget = await Reverse(client, item.Id, adjustments[0].Id, item.Version);
        Assert.Equal(HttpStatusCode.Created, exactlyTarget.StatusCode);
        item = await Item(client, item);
        Assert.Equal(1000m, item.AllocatedAmount);
    }

    [Fact]
    public async Task Given_unchanged_total_funding_When_saved_Then_no_entry_is_recorded_but_the_version_advances()
    {
        using var client = CreateClient();
        var item = await Create(client, 0m);
        Assert.Empty((await Ledger(client, item)).Entries);
        using var unchanged = await Allocate(client, item, 0m);
        unchanged.EnsureSuccessStatusCode();
        var saved = (await unchanged.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
        Assert.NotEqual(item.Version, saved.Version);
        Assert.Empty((await Ledger(client, item)).Entries);
        using var funded = await Allocate(client, saved, 1000m);
        funded.EnsureSuccessStatusCode();
        using var overTarget = await Pay(client, (await Item(client, item)), 0.01m, Today);
        Assert.Equal(HttpStatusCode.BadRequest, overTarget.StatusCode);
    }

    private HttpClient CreateClient() => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedClock());
    })).CreateClient();

    private static async Task<SavingsItemResponse> Create(HttpClient client, decimal allocation)
    {
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Car service", TargetAmount = 1000m, AllocatedAmount = allocation,
            Category = SavingsCategory.Transport,
            StartDate = new(2026, 12, 1), TargetDate = new(2027, 6, 1)
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
    }

    private static async Task<SavingsItemResponse> Item(HttpClient client, SavingsItemResponse item) =>
        (await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"))!;

    private static async Task<SavingsLedgerResponse> Ledger(HttpClient client, SavingsItemResponse item) =>
        (await client.GetFromJsonAsync<SavingsLedgerResponse>($"/api/savings-items/{item.Id}/ledger"))!;

    private static Task<HttpResponseMessage> Pay(HttpClient client, SavingsItemResponse item, decimal amount,
        DateOnly paidOn) =>
        client.PostAsJsonAsync($"/api/savings-items/{item.Id}/payments", new { amount, paidOn, version = item.Version });

    private static Task<HttpResponseMessage> Allocate(HttpClient client, SavingsItemResponse item, decimal amount) =>
        client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
            new { allocatedAmount = amount, version = item.Version });

    private static Task<HttpResponseMessage> Reverse(HttpClient client, Guid item, Guid entry, string version) =>
        client.PostAsJsonAsync($"/api/savings-items/{item}/ledger/{entry}/reversal", new { version });

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
