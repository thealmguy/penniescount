namespace PenniesCount.Core;

public sealed record SavingsGoal(decimal Target, decimal Allocated, DateOnly StartDate, DateOnly Deadline);
public enum ContributionStatus { Funded, NotStarted, Scheduled, NeededNow, Unavailable }
public sealed record Contribution(decimal? RequiredAmount, int? RemainingPayments, ContributionStatus Status);

public sealed class BudgetCalculator
{
    private readonly IContributionSchedule _schedule;
    public BudgetCalculator(IContributionSchedule schedule) =>
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));

    public Contribution Calculate(SavingsGoal goal, DateOnly asOf, bool paidToday = false)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.Target <= 0 || goal.Allocated < 0 || goal.Allocated > goal.Target || goal.Deadline < goal.StartDate)
            throw new ArgumentException("The savings goal has invalid amounts or dates.", nameof(goal));
        var remaining = goal.Target - goal.Allocated;
        if (remaining == 0) return new(0m, 0, ContributionStatus.Funded);
        if (goal.StartDate > asOf) return new(0m, 0, ContributionStatus.NotStarted);
        var payments = _schedule.CountRemainingPayments(asOf, goal.Deadline, paidToday);
        if (payments is null) return new(null, null, ContributionStatus.Unavailable);
        var paymentCount = payments.Value;
        if (paymentCount < 0) throw new InvalidOperationException("A contribution schedule returned a negative payment count.");
        return paymentCount == 0
            ? new(remaining, 0, ContributionStatus.NeededNow)
            : new(decimal.Ceiling(remaining * 100m / paymentCount) / 100m, paymentCount, ContributionStatus.Scheduled);
    }
}
