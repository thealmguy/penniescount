using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsDrawdownScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Given_a_funded_holiday_When_spending_in_parts_Then_funding_is_preserved_and_accounts_are_untouched()
    {
        using var client = factory.CreateClient();
        var account = await AccountScenarios.CreateAsync(client, 10000m);
        var before = await client.GetStringAsync($"/api/accounts/{account.Id}/balance-history");
        var item = await CreateGoal(client, 10000m);
        var original = item.Clone();
        foreach (var amount in new[] { 3000m, 4000m, 3000m })
        {
            using var response = await Spend(client, item, amount);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var resource = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
            item = await Item(client, item);
            Assert.Equal(10000m, item.GetProperty("targetAmount").GetDecimal());
            Assert.Equal(10000m, item.GetProperty("allocatedAmount").GetDecimal());
            Assert.False(item.GetProperty("isRemoved").GetBoolean());
            var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
            var row = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == Id(item));
            Assert.Equal(ContributionState.Funded, row.Status);
            Assert.Equal(0m, row.RemainingAmount);
            Assert.Equal(0m, row.RequiredContribution);
            Assert.Equal(10000m, dashboard.TotalSavings);
            Assert.Equal(item.GetProperty("availableAmount").GetDecimal(), dashboard.TotalAllocated);
            var purpose = dashboard.PurposeSummaries.Single(x => x.Category == SavingsCategory.HolidaysAndTravel);
            Assert.Equal(10000m, purpose.FundedAmount);
            Assert.Equal(item.GetProperty("drawnDownAmount").GetDecimal(), purpose.DrawnDownAmount);
            Assert.Equal(dashboard.TotalAllocated, purpose.AvailableAmount);
            Assert.Equal(10000m - dashboard.TotalAllocated, dashboard.UnallocatedBalance);
        }
        Assert.Equal(10000m, item.GetProperty("drawnDownAmount").GetDecimal());
        Assert.Equal(0m, item.GetProperty("availableAmount").GetDecimal());
        using var excess = await Spend(client, item, 0.01m);
        Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        Assert.Contains("Drawdowns cannot exceed total funded allocation.", await excess.Content.ReadAsStringAsync());
        using var stale = await Spend(client, original, 1m);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(account, await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}"));
        Assert.Equal(before, await client.GetStringAsync($"/api/accounts/{account.Id}/balance-history"));
    }

    [Fact]
    public async Task Given_partial_funding_When_correcting_and_deleting_spending_Then_only_available_funds_change()
    {
        using var client = factory.CreateClient();
        var item = await CreateGoal(client, 6000m);
        using var created = await Spend(client, item, 3000m);
        created.EnsureSuccessStatusCode();
        var path = created.Headers.Location!;
        item = await Item(client, item);
        using var excessiveCorrection = await client.PutAsJsonAsync(path, Payload(item, 6000.01m));
        Assert.Equal(HttpStatusCode.BadRequest, excessiveCorrection.StatusCode);
        using var fullCorrection = await client.PutAsJsonAsync(path, Payload(item, 6000m));
        fullCorrection.EnsureSuccessStatusCode();
        var original = path;
        path = Replacement(path, (await fullCorrection.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!);
        item = await Item(client, item);
        Assert.Equal(0m, item.GetProperty("availableAmount").GetDecimal());
        using var reused = await client.PutAsJsonAsync(original, Payload(item, 1m));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Contains("already been reversed", await reused.Content.ReadAsStringAsync());
        using var correction = await client.PutAsJsonAsync(path, Payload(item, 2000m));
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        path = Replacement(path, (await correction.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!);
        item = await Item(client, item);
        Assert.Equal(4000m, item.GetProperty("availableAmount").GetDecimal());
        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var row = dashboard.Groups.SelectMany(x => x.Items).Single(x => x.Item.Id == Id(item));
        Assert.Equal(4000m, row.RemainingAmount);
        Assert.NotEqual(ContributionState.Funded, row.Status);
        using var tooLow = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/allocation",
            new { allocatedAmount = 1999.99m, version = Version(item) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        Assert.Contains("Total funding must cover recorded drawdowns", await tooLow.Content.ReadAsStringAsync());
        using var delete = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(new { version = Version(item) })
        };
        using var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        item = await Item(client, item);
        Assert.Equal(6000m, item.GetProperty("allocatedAmount").GetDecimal());
        Assert.Equal(6000m, item.GetProperty("availableAmount").GetDecimal());
        Assert.Equal(0m, item.GetProperty("drawnDownAmount").GetDecimal());
        var reversed = (await client.GetFromJsonAsync<SavingsDrawdownResponse>(path))!;
        Assert.True(reversed.IsReversed);
        using var deleteAgain = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(new { version = Version(item) })
        };
        using var deletedAgain = await client.SendAsync(deleteAgain);
        Assert.Equal(HttpStatusCode.Conflict, deletedAgain.StatusCode);
        var history = (await client.GetFromJsonAsync<SavingsDrawdownHistoryResponse>(
            $"/api/savings-items/{Id(item)}/drawdowns"))!;
        Assert.Empty(history.Drawdowns);
        var ledger = (await client.GetFromJsonAsync<SavingsLedgerResponse>($"/api/savings-items/{Id(item)}/ledger"))!;
        Assert.Equal(7, ledger.Entries.Length);
        Assert.Equal(3, ledger.Entries.Count(x => x.Type == LedgerEntryType.Reversal));
        Assert.Equal(3, ledger.Entries.Count(x => x.Type == LedgerEntryType.Drawdown && x.IsReversed));
    }

    private static Uri Replacement(Uri path, SavingsDrawdownResponse replacement)
    {
        Assert.Equal(Guid.Parse(path.OriginalString[^36..]), replacement.ReplacesDrawdownId);
        Assert.False(replacement.IsReversed);
        return new Uri(path.OriginalString[..^36] + replacement.Id, UriKind.RelativeOrAbsolute);
    }

    [Fact]
    public async Task Given_an_underfunded_item_When_removed_Then_it_leaves_all_sums_and_retains_read_only_history()
    {
        using var client = factory.CreateClient();
        var item = await CreateGoal(client, 6000m);
        using var spent = await Spend(client, item, 1000m);
        spent.EnsureSuccessStatusCode();
        item = await Item(client, item);
        using var removed = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/removal",
            new { version = Version(item) });
        removed.EnsureSuccessStatusCode();
        item = await Item(client, item);
        Assert.True(item.GetProperty("isRemoved").GetBoolean());
        Assert.Equal(5000m, item.GetProperty("availableAmount").GetDecimal());
        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Equal(0m, dashboard.TotalAllocated);
        Assert.Equal(0m, dashboard.RequiredMonthlySavings);
        Assert.All(dashboard.Groups, x => Assert.Empty(x.Items));
        Assert.All(dashboard.PurposeSummaries, x => Assert.Equal(new(x.Category, 0m, 0m, 0m, 0m, 0m), x));
        var active = (await client.GetFromJsonAsync<SavingsItemsResponse>("/api/savings-items"))!;
        Assert.Empty(active.Items);
        var archive = (await client.GetFromJsonAsync<SavingsItemsResponse>("/api/savings-items?removed=true"))!;
        Assert.Single(archive.Items);
        using var history = JsonDocument.Parse(await client.GetStringAsync($"/api/savings-items/{Id(item)}/drawdowns"));
        Assert.Single(history.RootElement.GetProperty("drawdowns").EnumerateArray());
        using var rejected = await Spend(client, item, 1m);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("Removed items are read-only.", await rejected.Content.ReadAsStringAsync());
        using var correction = await client.PutAsJsonAsync(spent.Headers.Location, Payload(item, 1m));
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, spent.Headers.Location)
        {
            Content = JsonContent.Create(new { version = Version(item) })
        };
        using var deleted = await client.SendAsync(deletion);
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        using var repeated = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/removal",
            new { version = Version(item) });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var allocation = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/allocation",
            new { allocatedAmount = 6000m, version = Version(item) });
        Assert.Equal(HttpStatusCode.Conflict, allocation.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10000.01)]
    public async Task Given_invalid_spending_When_submitted_Then_it_does_not_change_the_item(decimal amount)
    {
        using var client = factory.CreateClient();
        var item = await CreateGoal(client, 10000m);
        using var response = await Spend(client, item, amount);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(item.Deserialize<SavingsItemResponse>(), (await Item(client, item)).Deserialize<SavingsItemResponse>());
    }

    [Fact]
    public async Task Given_date_and_text_boundaries_When_recording_spending_Then_the_local_date_and_money_rules_apply()
    {
        var today = new DateOnly(2027, 1, 15);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock(today));
        }));
        using var client = host.CreateClient();
        var item = await CreateGoal(client, 10000m);
        foreach (var payload in new object[]
        {
            new { amount = 1m, spentOn = DateOnly.MinValue, version = Version(item) },
            new { amount = 1m, spentOn = today.AddDays(1), version = Version(item) },
            new { amount = 1m, spentOn = today, description = new string('x', 201), version = Version(item) },
            new { amount = 10000000000000000m, spentOn = today, version = Version(item) },
            new { amount = 1m, spentOn = today, version = "invalid" }
        })
        {
            using var bad = await client.PostAsJsonAsync($"/api/savings-items/{Id(item)}/drawdowns", payload);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("application/problem+json", bad.Content.Headers.ContentType?.MediaType);
            if (payload.GetType().GetProperty("spentOn")?.GetValue(payload) is DateOnly date
                && (date == DateOnly.MinValue || date > today))
                Assert.Contains("Spending date is required and cannot be in the future.", await bad.Content.ReadAsStringAsync());
        }
        using var created = await client.PostAsJsonAsync($"/api/savings-items/{Id(item)}/drawdowns",
            new { amount = 0.01m, spentOn = today, description = "  Flights  ", version = Version(item) });
        created.EnsureSuccessStatusCode();
        var record = (await created.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!;
        Assert.Equal("Flights", record.Description);
        Assert.Equal(today, record.SpentOn);
        Assert.Equal(new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero), record.RecordedAtUtc);
        item = await Item(client, item);
        using var second = await client.PostAsJsonAsync($"/api/savings-items/{Id(item)}/drawdowns",
            new { amount = 0.02m, spentOn = today.AddDays(-1), description = (string?)null, version = Version(item) });
        second.EnsureSuccessStatusCode();
        var history = (await client.GetFromJsonAsync<SavingsDrawdownHistoryResponse>(
            $"/api/savings-items/{Id(item)}/drawdowns"))!;
        Assert.Equal(today, history.AsOfDate);
        Assert.Equal(0.03m, history.Item.DrawnDownAmount);
        Assert.Equal(new[] { today.AddDays(-1), today }, history.Drawdowns.Select(x => x.SpentOn));
        Assert.Null(history.Drawdowns[0].Description);
        Assert.Equal(Id(item), history.Item.Id);
    }

    [Fact]
    public async Task Given_two_items_When_using_missing_foreign_or_stale_ids_Then_writes_are_rejected_without_history_changes()
    {
        using var client = factory.CreateClient();
        var item = await CreateGoal(client, 6000m);
        var other = await CreateGoal(client, 6000m);
        using var spent = await Spend(client, item, 1m);
        spent.EnsureSuccessStatusCode();
        var record = (await spent.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!;
        var current = await Item(client, item);
        var path = $"/api/savings-items/{Id(item)}/drawdowns/{record.Id}";
        using var staleCorrection = await client.PutAsJsonAsync(path, Payload(item, 2m));
        Assert.Equal(HttpStatusCode.Conflict, staleCorrection.StatusCode);
        using var staleDelete = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(new { version = Version(item) })
        };
        using var staleDeleted = await client.SendAsync(staleDelete);
        Assert.Equal(HttpStatusCode.Conflict, staleDeleted.StatusCode);
        using var staleRemoval = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/removal",
            new { version = Version(item) });
        Assert.Equal(HttpStatusCode.Conflict, staleRemoval.StatusCode);
        using var allocation = await client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/allocation",
            new { allocatedAmount = 1m, version = Version(current) });
        allocation.EnsureSuccessStatusCode();
        current = await Item(client, item);
        Assert.Equal(0m, current.GetProperty("availableAmount").GetDecimal());
        foreach (var (owner, child) in new[] { (other, record.Id), (current, Guid.NewGuid()) })
        {
            var missingPath = $"/api/savings-items/{Id(owner)}/drawdowns/{child}";
            using var get = await client.GetAsync(missingPath);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            using var put = await client.PutAsJsonAsync(missingPath, Payload(owner, 1m));
            Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
            using var delete = new HttpRequestMessage(HttpMethod.Delete, missingPath)
            {
                Content = JsonContent.Create(new { version = Version(owner) })
            };
            using var deleted = await client.SendAsync(delete);
            Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        }
        var missingItem = Guid.NewGuid();
        using var history = await client.GetAsync($"/api/savings-items/{missingItem}/drawdowns");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        using var missingSpend = await client.PostAsJsonAsync($"/api/savings-items/{missingItem}/drawdowns", Payload(current, 1m));
        Assert.Equal(HttpStatusCode.NotFound, missingSpend.StatusCode);
        using var missingRemoval = await client.PutAsJsonAsync($"/api/savings-items/{missingItem}/removal",
            new { version = Version(current) });
        Assert.Equal(HttpStatusCode.NotFound, missingRemoval.StatusCode);
        using var missingDelete = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/savings-items/{missingItem}/drawdowns/{record.Id}")
        {
            Content = JsonContent.Create(new { version = Version(current) })
        };
        using var missingDeleted = await client.SendAsync(missingDelete);
        Assert.Equal(HttpStatusCode.NotFound, missingDeleted.StatusCode);
        Assert.Equal(1m, (await Item(client, current)).GetProperty("drawnDownAmount").GetDecimal());
    }

    [Fact]
    public async Task Given_history_with_tied_dates_When_read_or_corrected_Then_ordering_is_stable_and_corrections_append_a_replacement()
    {
        var clock = new HistoryClock();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        var item = await CreateGoal(client, 6000m);
        var records = new List<SavingsDrawdownResponse>();
        clock.Hour = 13;
        using var response = await Spend(client, item, 1m);
        response.EnsureSuccessStatusCode();
        records.Add((await response.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!);
        clock.Hour = 12;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
            foreach (var id in new[] { "00000000-0000-0000-0000-000000000002", "00000001-0000-0000-0000-000000000001" })
            {
                var tied = new SavingsLedgerEntry
                {
                    Id = Guid.Parse(id), SavingsItemId = Id(item), Type = LedgerEntryType.Drawdown, Amount = 1m,
                    EffectiveDate = new(2026, 1, 1), Description = "Flights", RecordedAtUtc = clock.GetUtcNow()
                };
                db.LedgerEntries.Add(tied);
                records.Add(tied.ToDrawdownResponse(false));
            }
            await db.SaveChangesAsync();
        }
        item = await Item(client, item);
        var history = (await client.GetFromJsonAsync<SavingsDrawdownHistoryResponse>(
            $"/api/savings-items/{Id(item)}/drawdowns"))!;
        Assert.Equal(records.OrderBy(x => x.SpentOn).ThenBy(x => x.RecordedAtUtc).ThenBy(x => x.Id), history.Drawdowns);
        clock.Hour = 14;
        using var updated = await client.PutAsJsonAsync(
            $"/api/savings-items/{Id(item)}/drawdowns/{records[0].Id}", Payload(item, 2m));
        updated.EnsureSuccessStatusCode();
        var corrected = (await updated.Content.ReadFromJsonAsync<SavingsDrawdownResponse>())!;
        Assert.NotEqual(records[0].Id, corrected.Id);
        Assert.Equal(records[0] with { Id = corrected.Id, Amount = 2m, RecordedAtUtc = clock.GetUtcNow(),
            ReplacesDrawdownId = records[0].Id }, corrected);
        var original = (await client.GetFromJsonAsync<SavingsDrawdownResponse>(
            $"/api/savings-items/{Id(item)}/drawdowns/{records[0].Id}"))!;
        Assert.Equal(records[0] with { IsReversed = true }, original);
        history = (await client.GetFromJsonAsync<SavingsDrawdownHistoryResponse>(
            $"/api/savings-items/{Id(item)}/drawdowns"))!;
        Assert.Equal(records.Skip(1).OrderBy(x => x.Id).Select(x => x.Id).Append(corrected.Id),
            history.Drawdowns.Select(x => x.Id));
        var ledger = (await client.GetFromJsonAsync<SavingsLedgerResponse>($"/api/savings-items/{Id(item)}/ledger"))!;
        var reversal = ledger.Entries.Single(x => x.Type == LedgerEntryType.Reversal);
        Assert.Equal(new LedgerEntryResponse(reversal.Id, LedgerEntryType.Reversal, 1m, records[0].SpentOn, null,
            clock.GetUtcNow(), false, records[0].Id, null, false), reversal);
        Assert.Equal(4m, history.Item.DrawnDownAmount);
    }

    [Theory]
    [InlineData("DELETE", "/drawdowns/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/removal")]
    public async Task Given_invalid_version_payloads_When_deleting_or_removing_Then_validation_precedes_item_access(
        string method, string suffix)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/savings-items/{Guid.NewGuid()}{suffix}")
        {
            Content = JsonContent.Create(new { version = "invalid" })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Version", await response.Content.ReadAsStringAsync());
    }

    private sealed class HistoryClock : TimeProvider
    {
        public int Hour { get; set; }
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 15, Hour, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Given_concurrent_item_writers_When_spending_or_removing_Then_only_one_commits_atomically(bool remove)
    {
        var gate = new SaveGate();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<SavingsDbContext>(options => options.AddInterceptors(gate))));
        using var client = host.CreateClient();
        var item = await CreateGoal(client, 10000m);
        gate.Armed = true;
        var spending = Spend(client, item, 10000m);
        var competing = remove
            ? client.PutAsJsonAsync($"/api/savings-items/{Id(item)}/removal", new { version = Version(item) })
            : Spend(client, item, 10000m);
        var responses = await Task.WhenAll(spending, competing);
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var history = (await client.GetFromJsonAsync<SavingsDrawdownHistoryResponse>(
                $"/api/savings-items/{Id(item)}/drawdowns"))!;
            if (history.Item.IsRemoved)
            {
                Assert.Empty(history.Drawdowns);
                Assert.Equal(0m, history.Item.DrawnDownAmount);
            }
            else
            {
                Assert.Single(history.Drawdowns);
                Assert.Equal(10000m, history.Item.DrawnDownAmount);
            }
            Assert.NotEqual(Version(item), history.Item.Version);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiting;
        public bool Armed { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                if (Interlocked.Increment(ref _waiting) == 2) _ready.SetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private static async Task<JsonElement> CreateGoal(HttpClient client, decimal allocation)
    {
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Holiday example", TargetAmount = 10000m, AllocatedAmount = allocation,
            Category = SavingsCategory.HolidaysAndTravel,
            StartDate = new(2026, 1, 1), TargetDate = new(2027, 4, 1)
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();
    private static string Version(JsonElement item) => item.GetProperty("version").GetString()!;
    private static object Payload(JsonElement item, decimal amount) =>
        new { amount, spentOn = new DateOnly(2026, 1, 1), description = "Flights", version = Version(item) };
    private static Task<HttpResponseMessage> Spend(HttpClient client, JsonElement item, decimal amount) =>
        client.PostAsJsonAsync($"/api/savings-items/{Id(item)}/drawdowns", Payload(item, amount));
    private static async Task<JsonElement> Item(HttpClient client, JsonElement item) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/savings-items/{Id(item)}");
}
