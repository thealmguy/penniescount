using System.ComponentModel.DataAnnotations;

namespace PenniesCount.Contracts;

public enum SavingsTerm { Short, Medium, Long, Past }

public sealed class CreateSavingsItemRequest : IValidatableObject
{
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Money(positive: true)]
    public decimal TargetAmount { get; set; }

    [Money]
    public decimal AllocatedAmount { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate == DateOnly.MinValue)
            yield return new("Start date is required.", [nameof(StartDate)]);
        if (TargetDate == DateOnly.MinValue)
            yield return new("Target date is required.", [nameof(TargetDate)]);
        if (TargetDate < StartDate)
            yield return new("Target date must be on or after the start date.", [nameof(TargetDate)]);
        if (AllocatedAmount > TargetAmount)
            yield return new("Allocated amount cannot exceed the target.", [nameof(AllocatedAmount)]);
    }
}

public sealed class UpdateAllocationRequest
{
    [Money]
    public decimal AllocatedAmount { get; set; }

    [Required, RegularExpression("^[A-Za-z0-9+/]{11}=$")]
    public string Version { get; set; } = "";
}

public sealed record SavingsItemResponse(Guid Id, string Name, SavingsTerm Type, decimal TargetAmount,
    decimal AllocatedAmount, DateOnly StartDate, DateOnly TargetDate, string Version);

public sealed record SavingsItemsResponse(DateOnly AsOfDate, SavingsItemResponse[] Items);
