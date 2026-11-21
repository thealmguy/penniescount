using PenniesCount.Core;

namespace PenniesCount.Core.Tests;

public sealed class BudgetScenarios
{
    private readonly BudgetCalculator _calculator = new(new FirstOfMonthSchedule());

    [Theory]
    [InlineData("2027-01-15", "2027-01-01", "2027-04-15", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-04-01", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-03-31", 450, 2, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-02-10", 900, 1, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-31", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-02-01", "2027-01-01", "2027-04-15", 450, 2, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-15", "2027-04-15", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2027-01-15", "2027-01-20", "2027-04-15", 0, 0, ContributionStatus.NotStarted)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-14", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-01-15", "2027-01-01", "2027-01-15", 900, 0, ContributionStatus.NeededNow)]
    [InlineData("2027-01-15", "2027-01-01", "2027-02-01", 900, 1, ContributionStatus.Scheduled)]
    [InlineData("2027-12-15", "2027-01-01", "2028-03-01", 300, 3, ContributionStatus.Scheduled)]
    [InlineData("2028-02-29", "2028-02-01", "2028-03-01", 900, 1, ContributionStatus.Scheduled)]
    public void Given_a_savings_goal_When_calculating_Then_remaining_first_of_month_payments_determine_the_contribution(
        string today, string start, string deadline, decimal amount, int payments, ContributionStatus status)
    {
        var goal = new SavingsGoal(1200m, 300m, DateOnly.Parse(start), DateOnly.Parse(deadline));

        var result = _calculator.Calculate(goal, DateOnly.Parse(today));

        Assert.Equal(new Contribution(amount, payments, status), result);
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
    public void Given_the_last_supported_month_When_calculating_Then_no_date_overflow_occurs()
    {
        var result = _calculator.Calculate(new(100m, 0m, new(9999, 12, 1), DateOnly.MaxValue), new(9999, 12, 15));
        Assert.Equal(new Contribution(100m, 0, ContributionStatus.NeededNow), result);
    }

    [Fact]
    public void Given_an_alternative_schedule_When_calculating_Then_the_policy_can_be_replaced()
    {
        var calculator = new BudgetCalculator(new TwoPaymentSchedule());

        var result = calculator.Calculate(new(100m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15));

        Assert.Equal(new Contribution(50m, 2, ContributionStatus.Scheduled), result);
    }

    private sealed class TwoPaymentSchedule : IContributionSchedule
    {
        public int CountRemainingPayments(DateOnly asOf, DateOnly deadline) => 2;
    }
}
