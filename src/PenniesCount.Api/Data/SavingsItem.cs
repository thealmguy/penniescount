using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class SavingsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal TargetAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public bool IsRemoved { get; set; }
    public List<SavingsItemDrawdown> Drawdowns { get; set; } = [];
    public byte[] Version { get; set; } = [];

    public SavingsItemResponse ToResponse(DateOnly asOf)
    {
        var drawnDown = Drawdowns.Sum(x => x.Amount);
        return new(Id, Name, SavingsTermClassifier.Classify(TargetDate, asOf), TargetAmount,
            AllocatedAmount, StartDate, TargetDate, Convert.ToBase64String(Version))
        {
            DrawnDownAmount = drawnDown, AvailableAmount = AllocatedAmount - drawnDown, IsRemoved = IsRemoved
        };
    }
}
