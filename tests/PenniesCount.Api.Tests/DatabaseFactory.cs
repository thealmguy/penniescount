using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Api.Data;

namespace PenniesCount.Api.Tests;

public sealed class DatabaseFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"PenniesCount_Test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Savings",
            $@"Server=(localdb)\MSSQLLocalDB;Database={_database};Integrated Security=true;Encrypt=true;TrustServerCertificate=true");

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SavingsDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        if (db.Database.GetDbConnection().Database != _database)
            throw new InvalidOperationException("Refusing to delete an unexpected test database.");
        await db.Database.EnsureDeletedAsync();
        await DisposeAsync();
    }
}
