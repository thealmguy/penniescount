namespace PenniesCount.Core;

public sealed class FirstOfMonthSchedule : IContributionSchedule
{
    public int CountRemainingPayments(DateOnly asOf, DateOnly deadline) =>
        Math.Max(0, (deadline.Year - asOf.Year) * 12 + deadline.Month - asOf.Month);
}
