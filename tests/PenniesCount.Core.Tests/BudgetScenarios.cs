using PenniesCount.Core;

namespace PenniesCount.Core.Tests;

public sealed class BudgetScenarios
{
    private readonly BudgetCalculator _calculator = new(new WorkingDaySchedule(
        new(2027, 1, 1), new(2028, 12, 31), [new(2027, 1, 1), new(2028, 1, 3)]));

    [Theory]
    [InlineData("2027-01-15", "2027-01-01", "2027-04-15", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-04-01", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-03-31", 450, 2, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-02-10", 900, 1, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-31", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-02-01", "2027-01-01", "2027-04-15", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-15", "2027-04-15", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-20", "2027-04-15", 0, 0, ContributionStatus.NotStarted)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-14", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-15", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-01-15", "2027-01-01", "2027-02-01", 900, 1, ContributionStatus.Scheduled)]
    [InlineData("2027-12-15", "2027-01-01", "2028-03-01", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2028-02-29", "2028-02-01", "2028-03-01", 900, 1, ContributionStatus.Scheduled)]
    public void Given_a_savings_goal_When_calculating_Then_remaining_working_day_payments_determine_the_contribution(
        string today, string start, string deadline, decimal amount, int payments, ContributionStatus status)
    {
        var goal = new SavingsGoal(1200m, 300m, DateOnly.Parse(start), DateOnly.Parse(deadline));

        var result = _calculator.Calculate(goal, DateOnly.Parse(today));

        Assert.Equal(new Contribution(amount, payments, status), result);
    }

    [Fact]
    public void Given_a_payment_already_made_on_the_allocation_day_When_calculating_Then_today_is_excluded()
    {
        var goal = new SavingsGoal(1200m, 300m, new(2027, 1, 1), new(2027, 4, 1));

        var result = _calculator.Calculate(goal, new(2027, 2, 1), paidToday: true);

        Assert.Equal(new Contribution(450m, 2, ContributionStatus.Scheduled), result);
    }

    [Fact]
    public void Given_a_payment_flag_on_a_non_allocation_day_When_calculating_Then_future_dates_are_unchanged()
    {
        var goal = new SavingsGoal(1200m, 300m, new(2027, 1, 1), new(2027, 4, 1));

        var result = _calculator.Calculate(goal, new(2027, 1, 15), paidToday: true);

        Assert.Equal(new Contribution(300m, 3, ContributionStatus.Scheduled), result);
    }

    [Fact]
    public void Given_an_uncovered_deadline_When_calculating_Then_a_weekday_payment_is_estimated()
    {
        var goal = new SavingsGoal(1200m, 300m, new(2027, 1, 1), new(2029, 1, 1));

        var result = _calculator.Calculate(goal, new(2028, 12, 15));

        Assert.Equal(new Contribution(900m, 1, ContributionStatus.Scheduled), result);
    }

    [Fact]
    public void Given_an_overdue_goal_and_unavailable_holiday_data_When_calculating_Then_the_full_remainder_is_needed_now()
    {
        var calculator = new BudgetCalculator(WorkingDaySchedule.WeekdaysOnly);

        var result = calculator.Calculate(
            new(1200m, 300m, new(2027, 1, 1), new(2027, 1, 14)), new(2027, 1, 15));

        Assert.Equal(new Contribution(900m, 0, ContributionStatus.NeededNow), result);
    }

    [Fact]
    public void Given_an_alternative_schedule_with_no_estimate_When_calculating_Then_the_recommendation_is_unavailable()
    {
        var calculator = new BudgetCalculator(new FixedPaymentSchedule(null));

        var result = calculator.Calculate(new(100m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15));

        Assert.Equal(new Contribution(null, null, ContributionStatus.Unavailable), result);
    }

    [Fact]
    public void Given_a_fractional_penny_contribution_When_calculating_Then_it_rounds_up_without_mutating_allocations()
    {
        var goal = new SavingsGoal(100m, 0m, new(2027, 1, 1), new(2027, 4, 1));

        var result = _calculator.Calculate(goal, new(2027, 1, 15));

        Assert.Equal(33.34m, result.RequiredAmount);
        Assert.Equal(0m, goal.Allocated);
    }

    [Fact]
    public void Given_a_fully_allocated_future_goal_When_calculating_Then_nothing_more_is_required()
    {
        var result = _calculator.Calculate(new(100m, 100m, new(2027, 3, 1), new(2027, 4, 1)), new(2027, 1, 15));
        Assert.Equal(new Contribution(0m, 0, ContributionStatus.Funded), result);
    }

    [Fact]
    public void Given_an_alternative_schedule_When_calculating_Then_the_policy_can_be_replaced()
    {
        var calculator = new BudgetCalculator(new TwoPaymentSchedule());

        var result = calculator.Calculate(new(100m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15));

        Assert.Equal(new Contribution(50m, 2, ContributionStatus.Scheduled), result);
    }

    [Fact]
    public void Given_an_invalid_goal_When_calculating_Then_it_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(new(100m, 0m, new(2027, 2, 1), new(2027, 1, 1)), new(2027, 1, 15)));

        Assert.Contains("The savings goal has invalid amounts or dates.", exception.Message);
        Assert.Equal("goal", exception.ParamName);
    }

    [Fact]
    public void Given_a_null_goal_When_calculating_Then_it_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Calculate(null!, new(2027, 1, 15)));
    }

    [Fact]
    public void Given_a_zero_target_When_calculating_Then_the_goal_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(new(0m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15)));
    }

    [Theory]
    [InlineData(100, -1)]
    [InlineData(100, 101)]
    public void Given_invalid_allocation_amounts_When_calculating_Then_the_goal_is_rejected(decimal target, decimal allocated)
    {
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(new(target, allocated, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15)));
    }

    [Fact]
    public void Given_a_goal_with_equal_start_and_deadline_When_fully_allocated_Then_it_is_valid()
    {
        var result = _calculator.Calculate(new(100m, 100m, new(2027, 1, 1), new(2027, 1, 1)), new(2027, 1, 15));

        Assert.Equal(new Contribution(0m, 0, ContributionStatus.Funded), result);
    }

    [Fact]
    public void Given_a_negative_payment_count_When_calculating_Then_the_schedule_is_rejected()
    {
        var calculator = new BudgetCalculator(new FixedPaymentSchedule(-1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            calculator.Calculate(new(100m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15)));

        Assert.Equal("A contribution schedule returned a negative payment count.", exception.Message);
    }

    private sealed class TwoPaymentSchedule : IContributionSchedule
    {
        public int? CountRemainingPayments(DateOnly asOf, DateOnly deadline, bool paidToday) => 2;
    }

    private sealed class FixedPaymentSchedule(int? count) : IContributionSchedule
    {
        public int? CountRemainingPayments(DateOnly asOf, DateOnly deadline, bool paidToday) => count;
    }
}
