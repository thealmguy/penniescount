using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsItemDrawdown
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SavingsItemId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly SpentOn { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }

    public SavingsDrawdownResponse ToResponse() => new(Id, Amount, SpentOn, Description, RecordedAtUtc);
}
