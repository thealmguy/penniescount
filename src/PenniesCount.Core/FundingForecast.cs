namespace PenniesCount.Core;

public sealed record ForecastPayment(DateOnly Date, decimal Amount);
public sealed record GoalForecast(ForecastPayment[] Payments, bool NeededNow);

public sealed class FundingForecast(WorkingDaySchedule schedule)
{
    private readonly BudgetCalculator _calculator = new(schedule);

    public GoalForecast Calculate(SavingsGoal goal, DateOnly asOf, bool paidToday = false)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var from = DateOnly.FromDayNumber(Math.Max(goal.StartDate.DayNumber, asOf.DayNumber));
        var excludeToday = paidToday && from == asOf;
        var contribution = _calculator.Calculate(goal, from, excludeToday);
        if (contribution.Status == ContributionStatus.Funded) return new([], false);
        var remaining = goal.Target - goal.Allocated;
        if (contribution.Status == ContributionStatus.NeededNow)
            return new([new(asOf, remaining)], true);

        var payments = new List<ForecastPayment>();
        foreach (var date in schedule.RemainingPaymentDates(from, goal.Deadline, excludeToday))
        {
            var amount = Math.Min(contribution.RequiredAmount!.Value, remaining);
            payments.Add(new(date, amount));
            remaining -= amount;
            if (remaining == 0) break;
        }
        return new(payments.ToArray(), false);
    }
}
