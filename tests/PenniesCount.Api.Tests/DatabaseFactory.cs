using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;

namespace PenniesCount.Api.Tests;

public sealed class DatabaseFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const int PoolSize = 64;
    private static readonly Lazy<string> SchemaKey = new(ComputeSchemaKey);
    private readonly bool _dedicated;
    private bool _disposed;
    private string? _database;
    private FileStream? _lease;

    public DatabaseFactory() : this(false)
    {
    }

    private DatabaseFactory(bool dedicated)
    {
        _dedicated = dedicated;
        if (dedicated) _database = $"PenniesCount_Test_{Guid.NewGuid():N}";
    }

    // For tests that rewrite the schema; pooled databases must always stay at the latest migration.
    public static DatabaseFactory Dedicated() => new(true);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_database is null)
            throw new InvalidOperationException("Initialize a pooled DatabaseFactory before using it.");
        builder.UseSetting("ConnectionStrings:Savings",
            $@"Server=(localdb)\MSSQLLocalDB;Database={_database};Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBankHolidayProvider>();
            services.AddSingleton<IBankHolidayProvider>(new FakeBankHolidayProvider());
        });
    }

    public async ValueTask InitializeAsync()
    {
        if (!_dedicated) _database = LeasePooledDatabase();
        using (var scope = Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SavingsDbContext>().Database;
            if (_dedicated || (await database.GetPendingMigrationsAsync()).Any()) await database.MigrateAsync();
        }
        if (!_dedicated) await ResetAsync();
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        VerifyDatabase(db);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LedgerEntries.ExecuteDeleteAsync();
        await db.SavingsItems.ExecuteDeleteAsync();
        await db.BalanceSnapshots.ExecuteDeleteAsync();
        await db.Accounts.ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_dedicated)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
                VerifyDatabase(db);
                await db.Database.EnsureDeletedAsync();
            }
            await base.DisposeAsync();
        }
        finally
        {
            _lease?.Dispose();
        }
    }

    private void VerifyDatabase(SavingsDbContext db)
    {
        if (db.Database.GetDbConnection().Database != _database)
            throw new InvalidOperationException("Refusing to reset or delete an unexpected test database.");
    }

    // Each process holds an exclusive file lock on its slot, so concurrent test runs never share a database.
    // The OS releases the lock if the process dies. Keying the pool by migration IDs keeps a branch with a
    // different migration history off another branch's databases.
    private string LeasePooledDatabase()
    {
        var locks = Path.Combine(Path.GetTempPath(), "PenniesCount.Tests");
        Directory.CreateDirectory(locks);
        for (var slot = 0; slot < PoolSize; slot++)
        {
            var database = $"PenniesCount_TestPool_{SchemaKey.Value}_{slot:D2}";
            try
            {
                _lease = new FileStream(Path.Combine(locks, $"{database}.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                return database;
            }
            catch (IOException)
            {
            }
        }
        throw new InvalidOperationException($"All {PoolSize} pooled test databases are in use.");
    }

    private static string ComputeSchemaKey()
    {
        var migrations = typeof(SavingsDbContext).Assembly.GetTypes()
            .Select(x => x.GetCustomAttribute<MigrationAttribute>()?.Id).OfType<string>().Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', migrations))))[..12];
    }

    private sealed class FakeBankHolidayProvider : IBankHolidayProvider
    {
        private static readonly BankHolidayCalendar Calendar = new(
            new HashSet<DateOnly>
            {
                new(2026, 1, 1), new(2026, 4, 3), new(2026, 4, 6), new(2026, 5, 4),
                new(2026, 5, 25), new(2026, 8, 31), new(2026, 12, 25), new(2026, 12, 28),
                new(2027, 1, 1), new(2027, 3, 26), new(2027, 3, 29), new(2027, 5, 3),
                new(2027, 5, 31), new(2027, 8, 30), new(2027, 12, 27), new(2027, 12, 28),
                new(2028, 1, 3), new(2028, 4, 14), new(2028, 4, 17), new(2028, 5, 1),
                new(2028, 5, 29), new(2028, 8, 28), new(2028, 12, 25), new(2028, 12, 26)
            },
            new(2026, 1, 1), new(2028, 12, 31));

        public Task<HolidayCalendarStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HolidayCalendarStatus(Calendar, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                false, null));
    }
}
