namespace PenniesCount.Core;

public interface IContributionSchedule
{
    int? CountRemainingPayments(DateOnly asOf, DateOnly deadline, bool paidToday);
}
