namespace PenniesCount.Core;

public sealed class WorkingDaySchedule : IContributionSchedule
{
    public static WorkingDaySchedule WeekdaysOnly { get; } = new(DateOnly.MinValue, DateOnly.MaxValue, Array.Empty<DateOnly>());
    private static readonly DateOnly LastMonth = new(DateOnly.MaxValue.Year, DateOnly.MaxValue.Month, 1);

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
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var count = 0;
        while (month <= deadline)
        {
            if (AllocationDateInMonth(month) is { } date && date <= deadline
                && (date > asOf || (date == asOf && !paidToday)))
                count++;

            if (month == LastMonth) break;
            month = month.AddMonths(1);
        }
        return count;
    }

    public DateOnly? NextAllocationDate(DateOnly asOf)
    {
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        while (true)
        {
            if (AllocationDateInMonth(month) is { } date && date >= asOf) return date;
            if (month == LastMonth) return null;
            month = month.AddMonths(1);
        }
    }

    private DateOnly? AllocationDateInMonth(DateOnly month)
    {
        DateOnly? firstWeekday = null;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        for (var offset = 0; offset < daysInMonth; offset++)
        {
            var date = month.AddDays(offset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            firstWeekday ??= date;
            if (date < _coveredFrom || date > _coveredThrough) return firstWeekday;
            if (!_holidays.Contains(date)) return date;
        }
        return null;
    }
}
