using Microsoft.EntityFrameworkCore;

namespace PenniesCount.Api.Data;

public sealed class SavingsDbContext(DbContextOptions<SavingsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountBalanceSnapshot> BalanceSnapshots => Set<AccountBalanceSnapshot>();
    public DbSet<SavingsItem> SavingsItems => Set<SavingsItem>();
    public DbSet<SavingsItemDrawdown> Drawdowns => Set<SavingsItemDrawdown>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SavingsItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetAmount).HasPrecision(18, 2);
            entity.Property(x => x.AllocatedAmount).HasPrecision(18, 2);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasMany(x => x.Drawdowns).WithOne().HasForeignKey(x => x.SavingsItemId);
            entity.ToTable("SavingsItems", table =>
            {
                table.HasCheckConstraint("CK_Items_Target", "[TargetAmount] > 0");
                table.HasCheckConstraint("CK_Items_Allocation", "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [TargetAmount]");
                table.HasCheckConstraint("CK_Items_Dates", "[TargetDate] >= [StartDate]");
            });
        });
        model.Entity<SavingsItemDrawdown>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(200);
            entity.HasIndex(x => new { x.SavingsItemId, x.SpentOn, x.RecordedAtUtc, x.Id });
            entity.ToTable("Drawdowns", table =>
                table.HasCheckConstraint("CK_Drawdowns_Amount", "[Amount] > 0"));
        });
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
