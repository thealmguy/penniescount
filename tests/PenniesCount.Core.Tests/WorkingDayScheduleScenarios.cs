using PenniesCount.Core;

namespace PenniesCount.Core.Tests;

public sealed class WorkingDayScheduleScenarios
{
    [Theory]
    [InlineData("2027-01-15", "2027-02-01", false, 1)]
    [InlineData("2027-04-30", "2027-05-03", false, 0)]
    [InlineData("2027-04-30", "2027-05-04", false, 1)]
    [InlineData("2028-01-01", "2028-01-04", false, 1)]
    [InlineData("2028-01-04", "2028-01-04", false, 1)]
    [InlineData("2028-01-04", "2028-01-04", true, 0)]
    [InlineData("2028-01-05", "2028-01-31", false, 0)]
    public void Given_official_holidays_When_counting_Then_only_eligible_working_days_are_included(
        string today, string deadline, bool paidToday, int expected)
    {
        var schedule = new WorkingDaySchedule(
            new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31),
            [new(2027, 5, 3), new(2028, 1, 3)]);

        Assert.Equal(expected, schedule.CountRemainingPayments(DateOnly.Parse(today), DateOnly.Parse(deadline), paidToday));
    }

    [Theory]
    [InlineData("2028-01-02", 0)]
    [InlineData("2028-01-03", 1)]
    public void Given_mixed_coverage_When_counting_Then_known_holidays_and_the_fallback_deadline_are_respected(
        string deadline, int expected)
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 1), new(2027, 12, 31), [new(2027, 12, 1)]);

        Assert.Equal(expected, schedule.CountRemainingPayments(new(2027, 12, 2), DateOnly.Parse(deadline), true));
    }

    [Fact]
    public void Given_a_schedule_covered_only_on_the_allocation_day_When_counting_Then_the_coverage_boundary_is_inclusive()
    {
        var schedule = new WorkingDaySchedule(new(2027, 2, 1), new(2027, 2, 1), []);

        Assert.Equal(1, schedule.CountRemainingPayments(new(2027, 2, 1), new(2027, 2, 1), false));
        Assert.Equal(new DateOnly(2027, 2, 1), schedule.NextAllocationDate(new(2027, 2, 1)));
    }

    [Fact]
    public void Given_the_last_supported_month_When_counting_Then_it_does_not_overflow()
    {
        var schedule = new WorkingDaySchedule(new(9999, 1, 1), DateOnly.MaxValue, []);

        Assert.Equal(0, schedule.CountRemainingPayments(new(9999, 12, 15), DateOnly.MaxValue, false));
        Assert.Null(schedule.NextAllocationDate(new(9999, 12, 15)));
    }

    [Fact]
    public void Given_every_workday_of_the_last_supported_month_is_a_holiday_When_counting_or_finding_next_Then_it_does_not_overflow()
    {
        var holidays = Enumerable.Range(1, 31)
            .Select(day => new DateOnly(9999, 12, day))
            .Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        var schedule = new WorkingDaySchedule(new(9999, 12, 1), DateOnly.MaxValue, holidays);

        Assert.Equal(0, schedule.CountRemainingPayments(DateOnly.MaxValue, DateOnly.MaxValue, false));
        Assert.Null(schedule.NextAllocationDate(DateOnly.MaxValue));
    }

    [Fact]
    public void Given_the_start_of_a_supported_year_When_finding_next_allocation_Then_the_month_advances_without_year_overflow()
    {
        var schedule = new WorkingDaySchedule(new(9999, 1, 1), DateOnly.MaxValue, []);

        Assert.Equal(new DateOnly(9999, 2, 1), schedule.NextAllocationDate(new(9999, 1, 15)));
    }

    [Theory]
    [InlineData("2027-01-01", "2027-01-04")]
    [InlineData("2027-01-04", "2027-01-04")]
    [InlineData("2027-01-05", "2027-02-01")]
    [InlineData("2027-08-01", "2027-08-02")]
    [InlineData("2028-01-01", "2028-01-04")]
    public void Given_a_date_When_finding_the_next_allocation_Then_weekends_and_bank_holidays_are_skipped(
        string asOfText, string expectedText)
    {
        var schedule = new WorkingDaySchedule(
            new(2027, 1, 1), new(2028, 12, 31), [new(2027, 1, 1), new(2028, 1, 3)]);

        Assert.Equal(DateOnly.Parse(expectedText), schedule.NextAllocationDate(DateOnly.Parse(asOfText)));
    }

    [Fact]
    public void Given_an_uncovered_next_allocation_When_finding_the_date_Then_it_is_the_first_weekday()
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 1), new(2027, 12, 31), []);

        Assert.Equal(new DateOnly(2028, 1, 3), schedule.NextAllocationDate(new(2027, 12, 15)));
    }

    [Fact]
    public void Given_holiday_coverage_ends_mid_month_When_finding_the_date_Then_it_falls_back_to_the_first_weekday()
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 1), new(2027, 1, 1), [new(2027, 1, 1)]);

        Assert.Equal(new DateOnly(2027, 1, 1), schedule.NextAllocationDate(new(2027, 1, 1)));
    }

    [Fact]
    public void Given_coverage_ends_on_the_first_non_holiday_weekday_When_scheduling_Then_that_date_is_used()
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 1), new(2027, 12, 2), [new(2027, 12, 1)]);

        Assert.Equal(new DateOnly(2027, 12, 2), schedule.NextAllocationDate(new(2027, 12, 1)));
    }
    [Fact]
    public void Given_no_calendar_When_scheduling_Then_the_first_weekday_counts_unless_already_paid()
    {
        var schedule = WorkingDaySchedule.WeekdaysOnly;
        var monday = new DateOnly(2028, 1, 3);

        Assert.Equal(monday, schedule.NextAllocationDate(new(2028, 1, 1)));
        Assert.Equal(1, schedule.CountRemainingPayments(monday, monday, false));
        Assert.Equal(0, schedule.CountRemainingPayments(monday, new(2028, 1, 4), true));
    }

    [Fact]
    public void Given_invalid_coverage_or_holiday_input_When_creating_a_schedule_Then_it_is_rejected()
    {
        var reversedCoverage = Assert.Throws<ArgumentException>(() =>
            new WorkingDaySchedule(new(2027, 2, 2), new(2027, 2, 1), []));
        Assert.Contains("Holiday coverage ends before it starts.", reversedCoverage.Message);
        var nullHolidays = Assert.Throws<ArgumentNullException>(() =>
            new WorkingDaySchedule(new(2027, 1, 1), new(2027, 12, 31), null!));
        Assert.Equal("holidays", nullHolidays.ParamName);
    }

    [Fact]
    public void Given_an_overdue_deadline_When_counting_Then_it_has_no_remaining_payments()
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 1), new(2027, 12, 31), []);

        Assert.Equal(0, schedule.CountRemainingPayments(new(2027, 1, 15), new(2027, 1, 14), false));
        Assert.Equal(0, schedule.CountRemainingPayments(new(2027, 1, 15), new(2027, 1, 15), true));
    }

    [Fact]
    public void Given_a_paid_deadline_today_When_coverage_starts_today_Then_the_known_remaining_count_is_zero()
    {
        var schedule = new WorkingDaySchedule(new(2027, 1, 15), new(2027, 1, 31), []);

        Assert.Equal(0, schedule.CountRemainingPayments(new(2027, 1, 15), new(2027, 1, 15), true));
    }
}
