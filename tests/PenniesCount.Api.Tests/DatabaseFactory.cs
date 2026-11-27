using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;

namespace PenniesCount.Api.Tests;

public sealed class DatabaseFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"PenniesCount_Test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Savings",
            $@"Server=(localdb)\MSSQLLocalDB;Database={_database};Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBankHolidayProvider>();
            services.AddSingleton<IBankHolidayProvider>(new FakeBankHolidayProvider());
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SavingsDbContext>().Database.MigrateAsync();
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

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        VerifyDatabase(db);
        await db.Database.EnsureDeletedAsync();
        await DisposeAsync();
    }

    private void VerifyDatabase(SavingsDbContext db)
    {
        if (db.Database.GetDbConnection().Database != _database)
            throw new InvalidOperationException("Refusing to reset or delete an unexpected test database.");
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
