using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public SavingsCategory? Category { get; set; }
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
            Category = Category,
            DrawnDownAmount = drawnDown, AvailableAmount = AllocatedAmount - drawnDown, IsRemoved = IsRemoved
        };
    }

    public static SavingsItem Open(CreateSavingsItemRequest request, DateOnly asOf, DateTimeOffset now)
    {
        var item = new SavingsItem
        {
            Name = request.Name.Trim(), Category = request.Category, TargetAmount = request.TargetAmount,
            StartDate = request.StartDate, TargetDate = request.TargetDate
        };
        if (request.AllocatedAmount > 0)
            item.Record(LedgerEntryType.OpeningFunding, request.AllocatedAmount, asOf, "Initial allocation", now);
        return item;
    }

    public static LedgerRuleViolation? CheckPaymentDate(DateOnly paidOn, DateOnly asOf) =>
        IsRecordableDate(paidOn, asOf) ? null : LedgerRuleViolation.Invalid(nameof(RecordPaymentRequest.PaidOn),
            "Payment date is required and cannot be in the future.", "Ledger");

    public static LedgerRuleViolation? CheckSpendingDate(DateOnly spentOn, DateOnly asOf) =>
        IsRecordableDate(spentOn, asOf) ? null : LedgerRuleViolation.Invalid(nameof(SaveDrawdownRequest.SpentOn),
            "Spending date is required and cannot be in the future.", "Drawdown date");

    private static bool IsRecordableDate(DateOnly date, DateOnly asOf) => date != DateOnly.MinValue && date <= asOf;

    public LedgerRuleViolation? CheckWritable(string version) =>
        Convert.ToBase64String(Version) != version ? LedgerRuleViolation.Conflict
            : IsRemoved ? LedgerRuleViolation.Removed : null;

    public SavingsLedgerEntry? FindDrawdown(Guid? id) =>
        Entries.SingleOrDefault(x => x.Id == id && x.Type == LedgerEntryType.Drawdown);

    public LedgerRuleViolation? RecordPayment(string version, decimal amount, DateOnly paidOn, DateTimeOffset now,
        out SavingsLedgerEntry? payment)
    {
        payment = null;
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        if (FundedAmount + amount > TargetAmount)
            return LedgerRuleViolation.Invalid(nameof(RecordPaymentRequest.Amount),
                "Payments cannot take total funding above the target.", "Ledger");
        payment = Record(LedgerEntryType.AllocationPayment, amount, paidOn, null, now);
        return null;
    }

    public LedgerRuleViolation? ReverseEntry(string version, Guid entryId, DateTimeOffset now,
        out SavingsLedgerEntry? reversal)
    {
        reversal = null;
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        var original = Entries.SingleOrDefault(x => x.Id == entryId);
        if (original is null) return LedgerRuleViolation.NotFound;
        if (original.Type == LedgerEntryType.Reversal)
            return LedgerRuleViolation.Invalid("EntryId", "Reversal entries cannot be reversed.", "Ledger");
        if (IsReversed(original)) return LedgerRuleViolation.AlreadyReversed;
        var funded = FundedAmount - original.FundingEffect;
        if (funded < DrawnDownAmount - original.DrawdownEffect || funded > TargetAmount)
            return LedgerRuleViolation.Invalid("EntryId",
                "Reversing this entry would take total funding below recorded drawdowns or above the target.", "Ledger");
        reversal = Reverse(original, now);
        return null;
    }

    public LedgerRuleViolation? RecordDrawdown(string version, Guid? replacing, decimal amount, DateOnly spentOn,
        string? description, DateTimeOffset now, out SavingsLedgerEntry? drawdown)
    {
        drawdown = null;
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        var existing = FindDrawdown(replacing);
        if (replacing.HasValue && existing is null) return LedgerRuleViolation.NotFound;
        if (existing is not null && IsReversed(existing)) return LedgerRuleViolation.AlreadyReversed;
        if (DrawnDownAmount - (existing?.Amount ?? 0m) + amount > FundedAmount)
            return LedgerRuleViolation.Invalid(nameof(SaveDrawdownRequest.Amount),
                "Drawdowns cannot exceed total funded allocation.", "Drawdown amount");
        if (existing is not null) Reverse(existing, now);
        drawdown = Record(LedgerEntryType.Drawdown, amount, spentOn, description?.Trim(), now, replaces: existing?.Id);
        return null;
    }

    public LedgerRuleViolation? ReverseDrawdown(string version, Guid drawdownId, DateTimeOffset now)
    {
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        var drawdown = FindDrawdown(drawdownId);
        if (drawdown is null) return LedgerRuleViolation.NotFound;
        if (IsReversed(drawdown)) return LedgerRuleViolation.AlreadyReversed;
        Reverse(drawdown, now);
        return null;
    }

    public LedgerRuleViolation? SetTotalFunding(string version, decimal total, DateOnly asOf, DateTimeOffset now)
    {
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        if (total > TargetAmount || total < DrawnDownAmount)
            return LedgerRuleViolation.Invalid(nameof(UpdateAllocationRequest.AllocatedAmount),
                "Total funding must cover recorded drawdowns and cannot exceed the target.", "Item allocation");
        var adjustment = total - FundedAmount;
        if (adjustment != 0)
            Record(LedgerEntryType.FundingAdjustment, adjustment, asOf, "Total funding updated", now);
        return null;
    }

    public LedgerRuleViolation? UpdateDetails(string version, string name, DateOnly targetDate,
        SavingsCategory? category)
    {
        var violation = CheckWritable(version);
        if (violation is not null) return violation;
        if (targetDate < StartDate)
            return LedgerRuleViolation.Invalid(nameof(UpdateSavingsItemDetailsRequest.TargetDate),
                "Target date must be on or after the start date.", "Item target date");
        Name = name.Trim();
        TargetDate = targetDate;
        Category = category;
        return null;
    }

    public LedgerRuleViolation? Remove(string version)
    {
        var violation = CheckWritable(version);
        if (violation is null) IsRemoved = true;
        return violation;
    }

    private SavingsLedgerEntry Reverse(SavingsLedgerEntry original, DateTimeOffset now) =>
        Record(LedgerEntryType.Reversal, original.Amount, original.EffectiveDate, null, now, reverses: original.Id);
}
