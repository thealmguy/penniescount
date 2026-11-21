using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public SavingsTerm Type { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public byte[] Version { get; set; } = [];

    public SavingsItemResponse ToResponse() =>
        new(Id, Name, Type, TargetAmount, AllocatedAmount, StartDate, TargetDate, Convert.ToBase64String(Version));
}
