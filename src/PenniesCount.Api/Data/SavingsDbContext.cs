using Microsoft.EntityFrameworkCore;

namespace PenniesCount.Api.Data;

public sealed class SavingsDbContext(DbContextOptions<SavingsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountBalanceSnapshot> BalanceSnapshots => Set<AccountBalanceSnapshot>();
    public DbSet<SavingsItem> SavingsItems => Set<SavingsItem>();
    public DbSet<SavingsLedgerEntry> LedgerEntries => Set<SavingsLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SavingsItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetAmount).HasPrecision(18, 2);
            entity.Property(x => x.AllocatedAmount).HasPrecision(18, 2);
            entity.Property(x => x.Version).IsRowVersion();
            entity.Ignore(x => x.EffectiveEntries);
            entity.Ignore(x => x.FundedAmount);
            entity.Ignore(x => x.DrawnDownAmount);
            entity.Ignore(x => x.History);
            entity.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.SavingsItemId);
            entity.ToTable("SavingsItems", table =>
            {
                table.HasCheckConstraint("CK_Items_Target", "[TargetAmount] > 0");
                table.HasCheckConstraint("CK_Items_Allocation", "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [TargetAmount]");
                table.HasCheckConstraint("CK_Items_Dates", "[TargetDate] >= [StartDate]");
            });
        });
        model.Entity<SavingsLedgerEntry>(entity =>
        {
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Description).HasMaxLength(200);
            entity.Ignore(x => x.FundingEffect);
            entity.Ignore(x => x.DrawdownEffect);
            entity.HasIndex(x => new { x.SavingsItemId, x.EffectiveDate, x.RecordedAtUtc, x.Id });
            entity.HasIndex(x => x.ReversesEntryId).IsUnique();
            entity.HasOne<SavingsLedgerEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SavingsLedgerEntry>().WithMany().HasForeignKey(x => x.ReplacesEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable("LedgerEntries", table =>
            {
                table.HasCheckConstraint("CK_Ledger_Type", "[Type] BETWEEN 0 AND 4");
                table.HasCheckConstraint("CK_Ledger_Amount",
                    "([Type] IN (2, 4) AND [Amount] <> 0) OR ([Type] NOT IN (2, 4) AND [Amount] > 0)");
                table.HasCheckConstraint("CK_Ledger_Reversal",
                    "([Type] = 4 AND [ReversesEntryId] IS NOT NULL) OR ([Type] <> 4 AND [ReversesEntryId] IS NULL)");
                table.HasCheckConstraint("CK_Ledger_Replacement", "[ReplacesEntryId] IS NULL OR [Type] = 3");
            });
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
