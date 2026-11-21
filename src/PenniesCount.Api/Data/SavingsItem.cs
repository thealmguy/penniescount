using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal TargetAmount { get; set; }
    // A constrained projection of the ledger's funding; it is recalculated whenever an entry is recorded.
    public decimal AllocatedAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public bool IsRemoved { get; set; }
    public List<SavingsLedgerEntry> Entries { get; set; } = [];
    public byte[] Version { get; set; } = [];

    public bool IsReversed(SavingsLedgerEntry entry) => Entries.Any(x => x.ReversesEntryId == entry.Id);

    public IEnumerable<SavingsLedgerEntry> EffectiveEntries =>
        Entries.Where(x => x.Type != LedgerEntryType.Reversal && !IsReversed(x));

    public decimal FundedAmount => EffectiveEntries.Sum(x => x.FundingEffect);
    public decimal DrawnDownAmount => EffectiveEntries.Sum(x => x.DrawdownEffect);

    public bool HasPaymentOn(DateOnly date) => EffectiveEntries.Any(x =>
        x.Type == LedgerEntryType.AllocationPayment && x.EffectiveDate == date);

    public IEnumerable<SavingsLedgerEntry> History => Entries.OrderBy(x => x.EffectiveDate)
        .ThenBy(x => x.RecordedAtUtc).ThenBy(x => x.Id);

    public SavingsLedgerEntry Record(LedgerEntryType type, decimal amount, DateOnly effectiveDate,
        string? description, DateTimeOffset recordedAtUtc, Guid? reverses = null, Guid? replaces = null)
    {
        var entry = new SavingsLedgerEntry
        {
            SavingsItemId = Id, Type = type, Amount = amount, EffectiveDate = effectiveDate,
            Description = description, RecordedAtUtc = recordedAtUtc, ReversesEntryId = reverses,
            ReplacesEntryId = replaces
        };
        Entries.Add(entry);
        AllocatedAmount = FundedAmount;
        return entry;
    }

    public SavingsItemResponse ToResponse(DateOnly asOf)
    {
        var drawnDown = DrawnDownAmount;
        return new(Id, Name, SavingsTermClassifier.Classify(TargetDate, asOf), TargetAmount,
            AllocatedAmount, StartDate, TargetDate, Convert.ToBase64String(Version))
        {
            DrawnDownAmount = drawnDown, AvailableAmount = AllocatedAmount - drawnDown, IsRemoved = IsRemoved
        };
    }
}
