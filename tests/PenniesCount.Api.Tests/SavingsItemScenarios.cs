using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Contracts;
using PenniesCount.Api.Data;

namespace PenniesCount.Api.Tests;

public sealed class SavingsItemScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Given_a_selected_purpose_category_When_creating_Then_it_is_persisted(int category)
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/savings-items", new
        {
            Name = "  Categorised example  ", TargetAmount = 100m, AllocatedAmount = 25m,
            StartDate = new DateOnly(2027, 1, 1), TargetDate = new DateOnly(2027, 4, 1), Category = category
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(category, json.GetProperty("category").GetInt32());
        Assert.Equal("Categorised example", json.GetProperty("name").GetString());
        var loaded = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(created.Headers.Location);
        Assert.Equal(category, loaded.GetProperty("category").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(9)]
    public async Task Given_a_missing_or_invalid_purpose_category_When_creating_Then_it_is_rejected(int? category)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/savings-items", new
        {
            Name = "Invalid category example", TargetAmount = 100m,
            StartDate = new DateOnly(2027, 1, 1), TargetDate = new DateOnly(2027, 4, 1), Category = category
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Category", out _));
    }

    [Fact]
    public async Task Given_an_omitted_purpose_category_When_creating_Then_it_is_rejected()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/savings-items", new
        {
            Name = "Missing category example", TargetAmount = 100m,
            StartDate = new DateOnly(2027, 1, 1), TargetDate = new DateOnly(2027, 4, 1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_an_item_When_editing_details_Then_only_metadata_changes_and_versions_protect_all_writes()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        var original = await CreateAsync(client, 25m);
        var ledger = (await client.GetFromJsonAsync<SavingsLedgerResponse>($"/api/savings-items/{original.Id}/ledger"))!;
        Assert.Equal("Initial allocation", Assert.Single(ledger.Entries).Description);
        var item = original;
        var deadline = new DateOnly(2033, 1, 16);
        foreach (var category in Enum.GetValues<SavingsCategory>())
        {
            using var edited = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details", new
            {
                Name = $"  Renamed {category}  ", TargetDate = deadline, Category = category, item.Version
            });
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var updated = (await edited.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
            Assert.NotEqual(item.Version, updated.Version);
            Assert.Equal(original with
            {
                Name = $"Renamed {category}", Category = category, TargetDate = deadline,
                Type = SavingsTerm.Long, Version = updated.Version
            }, updated);
            Assert.Equal(updated, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
            var afterLedger = (await client.GetFromJsonAsync<SavingsLedgerResponse>($"/api/savings-items/{item.Id}/ledger"))!;
            Assert.Equal(ledger.Entries, afterLedger.Entries);
            item = updated;
        }

        var budget = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var row = budget.Groups.Single(x => x.Type == SavingsTerm.Long).Items.Single(x => x.Item.Id == item.Id);
        Assert.Equal(item, row.Item);
        Assert.Equal(72, row.RemainingPayments);
        Assert.Equal(1.05m, row.RequiredContribution);
        using var staleDetails = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details", new
        {
            Name = "Stale", TargetDate = item.TargetDate, Category = SavingsCategory.Other, original.Version
        });
        using var staleAllocation = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
            new UpdateAllocationRequest { AllocatedAmount = 50m, Version = original.Version });
        using var stalePayment = await client.PostAsJsonAsync($"/api/savings-items/{item.Id}/payments",
            new RecordPaymentRequest { Amount = 1m, PaidOn = new(2027, 1, 15), Version = original.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleDetails.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleAllocation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stalePayment.StatusCode);
        Assert.Equal(item, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
        using var unchanged = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details",
            new UpdateSavingsItemDetailsRequest
            {
                Name = item.Name, TargetDate = item.TargetDate, Category = item.Category, Version = item.Version
            });
        unchanged.EnsureSuccessStatusCode();
        var sameDetails = (await unchanged.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
        Assert.NotEqual(item.Version, sameDetails.Version);
        Assert.Equal(item with { Version = sameDetails.Version }, sameDetails);
        using var boundary = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details",
            new UpdateSavingsItemDetailsRequest
            {
                Name = item.Name, TargetDate = item.StartDate, Category = item.Category, Version = sameDetails.Version
            });
        boundary.EnsureSuccessStatusCode();
        var past = (await boundary.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
        Assert.Equal(SavingsTerm.Past, past.Type);
        var pastBudget = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        var pastRow = pastBudget.Groups.Single(x => x.Type == SavingsTerm.Past).Items.Single(x => x.Item.Id == item.Id);
        Assert.Equal(75m, pastRow.RequiredContribution);
        Assert.Equal(ContributionState.NeededNow, pastRow.Status);
    }

    [Theory]
    [InlineData("", "2027-04-01", 1, "Name")]
    [InlineData("   ", "2027-04-01", 1, "Name")]
    [InlineData("Example", "0001-01-01", 1, "TargetDate")]
    [InlineData("Example", "2026-12-31", 1, "TargetDate")]
    [InlineData("Example", "2027-04-01", null, "Category")]
    [InlineData("Example", "2027-04-01", -1, "Category")]
    [InlineData("Example", "2027-04-01", 9, "Category")]
    public async Task Given_invalid_details_When_editing_Then_validation_leaves_the_item_unchanged(
        string name, string target, int? category, string field)
    {
        using var client = factory.CreateClient();
        var item = await CreateAsync(client, 25m);
        using var response = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details", new
        {
            Name = name, TargetDate = DateOnly.Parse(target), Category = category, item.Version
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        var errors = problem.GetProperty("errors").GetProperty(field);
        Assert.True(errors.GetArrayLength() > 0);
        Assert.All(errors.EnumerateArray(), error => Assert.False(string.IsNullOrWhiteSpace(error.GetString())));
        Assert.Equal(item, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
    }

    [Fact]
    public async Task Given_removed_or_missing_items_When_editing_details_Then_the_write_is_rejected()
    {
        using var client = factory.CreateClient();
        var item = await CreateAsync(client, 25m);
        using var removal = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/removal",
            new ItemVersionRequest { Version = item.Version });
        removal.EnsureSuccessStatusCode();
        item = (await removal.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
        var request = new { Name = "Changed", TargetDate = new DateOnly(2027, 4, 1), Category = 2, item.Version };
        using var removed = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details", request);
        using var missing = await client.PutAsJsonAsync($"/api/savings-items/{Guid.NewGuid()}/details", request);
        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(item, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
    }

    [Theory]
    [InlineData(101, "AAAAAAAAAAA=", "Name")]
    [InlineData(10, "", "Version")]
    [InlineData(10, "not-a-version", "Version")]
    public async Task Given_an_overlong_name_or_invalid_version_When_editing_details_Then_it_is_rejected(
        int nameLength, string version, string field)
    {
        using var client = factory.CreateClient();
        var item = await CreateAsync(client, 25m);
        using var response = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/details",
            new UpdateSavingsItemDetailsRequest
            {
                Name = new string('x', nameLength), TargetDate = item.TargetDate, Category = item.Category, Version = version
            });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(item, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
    }

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
            Assert.Equal(item with { AllocatedAmount = amount, AvailableAmount = amount, Version = updated.Version }, updated);
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

    public sealed class SavingsCategoryScenarios(DatabaseFactory factory) : IClassFixture<DatabaseFactory>
    {
        [Theory]
        [InlineData("2027-01-15", "2027-01-14", 3)]
        [InlineData("2027-01-15", "2027-01-15", 0)]
        [InlineData("2027-01-15", "2028-01-14", 0)]
        [InlineData("2027-01-15", "2028-01-15", 1)]
        [InlineData("2027-01-15", "2028-01-16", 1)]
        [InlineData("2027-01-15", "2032-01-14", 1)]
        [InlineData("2027-01-15", "2032-01-15", 1)]
        [InlineData("2027-01-15", "2032-01-16", 2)]
        [InlineData("2028-02-29", "2029-02-27", 0)]
        [InlineData("2028-02-29", "2029-02-28", 1)]
        [InlineData("2028-02-29", "2033-02-28", 1)]
        [InlineData("2028-02-29", "2033-03-01", 2)]
        [InlineData("9994-01-01", "9999-01-01", 1)]
        [InlineData("9994-01-01", "9999-12-31", 2)]
        [InlineData("9995-01-01", "9999-12-31", 1)]
        [InlineData("9999-01-01", "9999-12-31", 0)]
        [InlineData("9998-01-01", "9999-12-31", 1)]
        [InlineData("9999-12-31", "9999-12-31", 0)]
        public async Task Given_a_target_date_When_reading_or_writing_Then_every_response_uses_calendar_date_categories(
            string today, string target, int expected)
        {
            var clock = new CategoryClock(DateOnly.Parse(today));
            await using var host = WithClock(clock);
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
            {
                Name = "Boundary example",
                Category = SavingsCategory.Other,
                TargetAmount = 100m,
                StartDate = DateOnly.Parse(target),
                TargetDate = DateOnly.Parse(target)
            });
            created.EnsureSuccessStatusCode();
            var item = (await created.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
            Assert.Equal((SavingsTerm)expected, item.Type);
            Assert.Equal(item, await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{item.Id}"));
            var list = (await client.GetFromJsonAsync<SavingsItemsResponse>("/api/savings-items"))!;
            Assert.Equal(clock.Today, list.AsOfDate);
            Assert.Equal(item, list.Items.Single(x => x.Id == item.Id));
            Assert.Equal(list.Items.OrderBy(x => x.Type).ThenBy(x => x.TargetDate).ThenBy(x => x.Name).ThenBy(x => x.Id),
                list.Items);
            using var updated = await client.PutAsJsonAsync($"/api/savings-items/{item.Id}/allocation",
                new UpdateAllocationRequest { AllocatedAmount = 25m, Version = item.Version });
            updated.EnsureSuccessStatusCode();
            Assert.Equal((SavingsTerm)expected, (await updated.Content.ReadFromJsonAsync<SavingsItemResponse>())!.Type);
            var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
            Assert.Equal(clock.Today, dashboard.AsOfDate);
            Assert.Equal(new[] { SavingsTerm.Short, SavingsTerm.Medium, SavingsTerm.Long, SavingsTerm.Past },
                dashboard.Groups.Select(x => x.Type));
            Assert.Equal((SavingsTerm)expected,
                dashboard.Groups.Single(x => x.Items.Any(row => row.Item.Id == item.Id)).Type);
        }

        [Fact]
        public async Task Given_an_existing_goal_When_time_passes_Then_it_moves_categories_without_changing_allocations_or_versions()
        {
            var clock = new CategoryClock(new(2027, 1, 15));
            await using var host = WithClock(clock);
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/savings-items", new
            {
                Name = "Rollover example",
                Category = SavingsCategory.Other,
                Type = SavingsTerm.Short,
                TargetAmount = 100m,
                AllocatedAmount = 25m,
                StartDate = clock.Today,
                TargetDate = new DateOnly(2032, 1, 16)
            });
            created.EnsureSuccessStatusCode();
            var original = (await created.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
            Assert.Equal(SavingsTerm.Long, original.Type);
            foreach (var (date, type) in new[]
            {
                    (new DateOnly(2027, 1, 16), SavingsTerm.Medium),
                    (new DateOnly(2031, 1, 17), SavingsTerm.Short),
                    (new DateOnly(2032, 1, 17), SavingsTerm.Past)
                })
            {
                clock.Today = date;
                var loaded = (await client.GetFromJsonAsync<SavingsItemResponse>($"/api/savings-items/{original.Id}"))!;
                Assert.Equal(original with { Type = type }, loaded);
                var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
                Assert.Equal(loaded, dashboard.Groups.Single(x => x.Type == type).Items.Single(x => x.Item.Id == original.Id).Item);
            }
            var pastBudget = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
            var past = pastBudget.Groups.Single(x => x.Type == SavingsTerm.Past).Items.Single(x => x.Item.Id == original.Id);
            Assert.Equal(75m, past.RequiredContribution);
            Assert.Equal(ContributionState.NeededNow, past.Status);
            using var funded = await client.PutAsJsonAsync($"/api/savings-items/{original.Id}/allocation",
                new UpdateAllocationRequest { AllocatedAmount = 100m, Version = original.Version });
            funded.EnsureSuccessStatusCode();
            var fundedBudget = (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
            var fundedPast = fundedBudget.Groups.Single(x => x.Type == SavingsTerm.Past).Items.Single(x => x.Item.Id == original.Id);
            Assert.Equal(ContributionState.Funded, fundedPast.Status);
            Assert.Equal(0m, fundedPast.RequiredContribution);
            Assert.Equal(pastBudget.TotalAllocated + 75m, fundedBudget.TotalAllocated);
            Assert.Equal(pastBudget.UnallocatedBalance - 75m, fundedBudget.UnallocatedBalance);
            Assert.Equal(pastBudget.RequiredMonthlySavings - 75m, fundedBudget.RequiredMonthlySavings);
        }

        [Fact]
        public async Task Given_a_legacy_stored_category_When_migrating_Then_goal_data_is_preserved_and_category_is_derived()
        {
            var isolated = DatabaseFactory.Dedicated();
            try
            {
                using var scope = isolated.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20261121081053_SavingsItems");
                var id = Guid.NewGuid();
                var start = new DateOnly(2027, 1, 1);
                var target = new DateOnly(2027, 4, 1);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO SavingsItems (Id, Name, Type, TargetAmount, AllocatedAmount, StartDate, TargetDate)
                        VALUES ({id}, {"Legacy example"}, {2}, {100m}, {25m}, {start}, {target})
                        """);
                var before = await db.SavingsItems.AsNoTracking()
                    .Select(x => new { x.Id, x.Name, x.TargetAmount, x.AllocatedAmount, x.StartDate, x.TargetDate, x.Version })
                    .SingleAsync();

                await migrator.MigrateAsync();

                var after = (await db.SavingsItems.AsNoTracking().SingleAsync()).ToResponse(new(2027, 1, 15));
                Assert.Equal(before.Id, after.Id);
                Assert.Equal(before.Name, after.Name);
                Assert.Equal(Convert.ToBase64String(before.Version), after.Version);
                Assert.Equal(SavingsTerm.Short, after.Type);
                Assert.Null(after.Category);
                Assert.Equal(100m, after.TargetAmount);
                Assert.Equal(25m, after.AllocatedAmount);
                Assert.Equal(start, after.StartDate);
                Assert.Equal(target, after.TargetDate);
                Assert.False(db.Database.HasPendingModelChanges());
                using var client = isolated.CreateClient();
                using var edited = await client.PutAsJsonAsync($"/api/savings-items/{id}/details",
                    new UpdateSavingsItemDetailsRequest
                    {
                        Name = after.Name, TargetDate = start, Category = SavingsCategory.EmergencyFund, Version = after.Version
                    });
                edited.EnsureSuccessStatusCode();
                var categorised = (await edited.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
                Assert.Equal(SavingsCategory.EmergencyFund, categorised.Category);
                Assert.Equal(start, categorised.TargetDate);
                Assert.Equal(25m, categorised.AllocatedAmount);
            }
            finally
            {
                await isolated.DisposeAsync();
            }
        }

        private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithClock(TimeProvider clock) =>
            factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }));

        private sealed class CategoryClock(DateOnly today) : TimeProvider
        {
            public DateOnly Today { get; set; } = today;
            public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        }
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
            Name = "Example goal",
            Category = SavingsCategory.Other,
            TargetAmount = target,
            AllocatedAmount = allocation,
            StartDate = new(2027, 1, 1),
            TargetDate = new DateOnly(2027, 1, 1).AddDays(days)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_missing_dates_When_creating_Then_the_request_is_invalid()
    {
        using var client = factory.CreateClient();
        using var dates = await client.PostAsJsonAsync("/api/savings-items",
            new CreateSavingsItemRequest { Name = "Example goal", Category = SavingsCategory.Other, TargetAmount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, dates.StatusCode);
    }

    internal static async Task<SavingsItemResponse> CreateAsync(HttpClient client, decimal allocation)
    {
        using var response = await client.PostAsJsonAsync("/api/savings-items", new CreateSavingsItemRequest
        {
            Name = "Example goal",
            Category = SavingsCategory.Other,
            TargetAmount = 100m,
            AllocatedAmount = allocation,
            StartDate = new(2027, 1, 1),
            TargetDate = new(2027, 4, 1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavingsItemResponse>())!;
    }
}
