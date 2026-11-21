namespace PenniesCount.Core;

public sealed class WorkingDaySchedule : IContributionSchedule
{
    public static IContributionSchedule Unavailable { get; } = new UnavailableSchedule();

    private readonly DateOnly _coveredFrom;
    private readonly DateOnly _coveredThrough;
    private readonly HashSet<DateOnly> _holidays;

    public WorkingDaySchedule(DateOnly coveredFrom, DateOnly coveredThrough, IEnumerable<DateOnly> holidays)
    {
        if (coveredThrough < coveredFrom)
            throw new ArgumentException("Holiday coverage ends before it starts.", nameof(coveredThrough));
        ArgumentNullException.ThrowIfNull(holidays);
        _coveredFrom = coveredFrom;
        _coveredThrough = coveredThrough;
        _holidays = holidays.ToHashSet();
    }

    public int? CountRemainingPayments(DateOnly asOf, DateOnly deadline, bool paidToday)
    {
        if (deadline < asOf || (deadline == asOf && paidToday)) return 0;

        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var count = 0;
        while (month <= deadline)
        {
            var candidate = month;
            while (candidate <= deadline && candidate.Month == month.Month)
            {
                if (candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    if (candidate < _coveredFrom || candidate > _coveredThrough) return null;
                    if (!_holidays.Contains(candidate))
                    {
                        if (candidate > asOf || (candidate == asOf && !paidToday)) count++;
                        break;
                    }
                }

                if (candidate == DateOnly.MaxValue) break;
                candidate = candidate.AddDays(1);
            }

            if (month.Year == 9999 && month.Month == 12) break;
            month = month.AddMonths(1);
        }
        return count;
    }

    public DateOnly? NextAllocationDate(DateOnly asOf)
    {
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        while (true)
        {
            var candidate = month;
            while (candidate.Month == month.Month)
            {
                if (candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    if (candidate < _coveredFrom || candidate > _coveredThrough) return null;
                    if (!_holidays.Contains(candidate))
                    {
                        if (candidate >= asOf) return candidate;
                        break;
                    }
                }

                if (candidate == DateOnly.MaxValue) return null;
                candidate = candidate.AddDays(1);
            }

            if (month.Year == 9999 && month.Month == 12) return null;
            month = month.AddMonths(1);
        }
    }

    private sealed class UnavailableSchedule : IContributionSchedule
    {
        public int? CountRemainingPayments(DateOnly asOf, DateOnly deadline, bool paidToday) =>
            deadline < asOf || (deadline == asOf && paidToday) ? 0 : null;
    }
}
