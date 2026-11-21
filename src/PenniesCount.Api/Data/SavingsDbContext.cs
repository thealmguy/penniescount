using Microsoft.EntityFrameworkCore;

namespace PenniesCount.Api.Data;

public sealed class SavingsDbContext(DbContextOptions<SavingsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountBalanceSnapshot> BalanceSnapshots => Set<AccountBalanceSnapshot>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(entity =>
        {
            entity.Property(x => x.ProviderName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AccountName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TotalFunds).HasPrecision(18, 2);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasMany(x => x.History).WithOne().HasForeignKey(x => x.AccountId);
            entity.ToTable("Accounts", table =>
            {
                table.HasCheckConstraint("CK_Accounts_Funds", "[TotalFunds] >= 0");
                table.HasCheckConstraint("CK_Accounts_Type", "[Type] BETWEEN 0 AND 3");
            });
        });
        model.Entity<AccountBalanceSnapshot>(entity =>
        {
            entity.Property(x => x.TotalFunds).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.AccountId, x.RecordedAtUtc, x.Id });
            entity.ToTable("BalanceSnapshots", table =>
                table.HasCheckConstraint("CK_Snapshots_Funds", "[TotalFunds] >= 0"));
        });
    }
}
