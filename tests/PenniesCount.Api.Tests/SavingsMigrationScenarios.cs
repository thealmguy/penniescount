using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsMigrationScenarios : IAsyncLifetime
{
    private readonly DatabaseFactory factory = DatabaseFactory.Dedicated();
    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Given_pre_drawdown_data_When_migrating_Then_existing_items_are_active_with_their_funds_preserved()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261121093753_DateDerivedSavingsCategories");
        var id = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var deadline = new DateOnly(2027, 4, 1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO SavingsItems (Id, Name, TargetAmount, AllocatedAmount, StartDate, TargetDate)
            VALUES ({id}, {"Existing example"}, {10000m}, {6000m}, {start}, {deadline})
            """);
        await migrator.MigrateAsync();
        var item = await db.SavingsItems.AsNoTracking().Include(x => x.Entries).SingleAsync();
        Assert.Equal(id, item.Id);
        Assert.Equal(10000m, item.TargetAmount);
        Assert.Equal(6000m, item.AllocatedAmount);
        Assert.Equal(start, item.StartDate);
        Assert.Equal(deadline, item.TargetDate);
        Assert.False(item.IsRemoved);
        var opening = Assert.Single(item.Entries);
        Assert.Equal((LedgerEntryType.OpeningFunding, 6000m, start, true),
            (opening.Type, opening.Amount, opening.EffectiveDate, opening.IsInferred));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Given_drawdown_data_When_migrating_to_the_ledger_and_back_Then_funding_and_drawdowns_are_preserved()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261121102101_SavingsDrawdowns");
        var funded = Guid.NewGuid();
        var unfunded = Guid.NewGuid();
        var drawdown = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var spentOn = new DateOnly(2026, 3, 4);
        var recorded = new DateTimeOffset(2026, 3, 5, 9, 30, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO SavingsItems (Id, Name, TargetAmount, AllocatedAmount, StartDate, TargetDate, IsRemoved)
            VALUES ({funded}, {"Funded"}, {10000m}, {6000m}, {start}, {new DateOnly(2027, 4, 1)}, {true}),
                   ({unfunded}, {"Unfunded"}, {500m}, {0m}, {start}, {new DateOnly(2027, 4, 1)}, {false});
            INSERT INTO Drawdowns (Id, SavingsItemId, Amount, SpentOn, Description, RecordedAtUtc)
            VALUES ({drawdown}, {funded}, {1500m}, {spentOn}, {"Flights"}, {recorded});
            """);
        var versions = await db.SavingsItems.AsNoTracking().Select(x => new { x.Id, x.Version })
            .ToDictionaryAsync(x => x.Id, x => x.Version);
        await migrator.MigrateAsync();
        var items = await db.SavingsItems.AsNoTracking().Include(x => x.Entries).ToDictionaryAsync(x => x.Id);
        Assert.Empty(items[unfunded].Entries);
        var item = items[funded];
        Assert.Equal(versions[funded], item.Version);
        Assert.Equal(item.AllocatedAmount, item.FundedAmount);
        Assert.Equal(1500m, item.DrawnDownAmount);
        var spent = item.Entries.Single(x => x.Type == LedgerEntryType.Drawdown);
        Assert.Equal((drawdown, 1500m, spentOn, "Flights", recorded, false),
            (spent.Id, spent.Amount, spent.EffectiveDate, spent.Description, spent.RecordedAtUtc, spent.IsInferred));
        var opening = item.Entries.Single(x => x.Type == LedgerEntryType.OpeningFunding);
        Assert.Equal((6000m, start, true, "Opening balance inferred from existing total funding"),
            (opening.Amount, opening.EffectiveDate, opening.IsInferred, opening.Description));
        Assert.Equal(2, item.Entries.Count);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO LedgerEntries (Id, SavingsItemId, Type, Amount, EffectiveDate, RecordedAtUtc, IsInferred)
            VALUES ({Guid.NewGuid()}, {funded}, {3}, {100m}, {spentOn}, {recorded}, {false});
            INSERT INTO LedgerEntries (Id, SavingsItemId, Type, Amount, EffectiveDate, RecordedAtUtc, IsInferred, ReversesEntryId)
            SELECT {Guid.NewGuid()}, {funded}, {4}, {100m}, {spentOn}, {recorded}, {false}, Id
            FROM LedgerEntries WHERE Amount = {100m};
            """);
        await migrator.MigrateAsync("20261121102101_SavingsDrawdowns");
        var restored = await db.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM Drawdowns").ToListAsync();
        Assert.Equal([drawdown], restored);
        await migrator.MigrateAsync();
    }
}
