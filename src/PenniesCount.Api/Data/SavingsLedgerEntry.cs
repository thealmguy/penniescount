using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SavingsItemId { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public bool IsInferred { get; set; }
    public Guid? ReversesEntryId { get; set; }
    public Guid? ReplacesEntryId { get; set; }

    public decimal FundingEffect => Type is LedgerEntryType.OpeningFunding or LedgerEntryType.AllocationPayment
        or LedgerEntryType.FundingAdjustment ? Amount : 0m;

    public decimal DrawdownEffect => Type == LedgerEntryType.Drawdown ? Amount : 0m;

    public LedgerEntryResponse ToResponse(bool isReversed) => new(Id, Type, Amount, EffectiveDate, Description,
        RecordedAtUtc, IsInferred, ReversesEntryId, ReplacesEntryId, isReversed);

    public SavingsDrawdownResponse ToDrawdownResponse(bool isReversed) =>
        new(Id, Amount, EffectiveDate, Description, RecordedAtUtc)
        {
            ReplacesDrawdownId = ReplacesEntryId, IsReversed = isReversed
        };
}
