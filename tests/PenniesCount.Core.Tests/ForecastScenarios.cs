using PenniesCount.Core;

namespace PenniesCount.Core.Tests;

public sealed class ForecastScenarios
{
    private readonly FundingForecast _forecast = new(new WorkingDaySchedule(
        new(2027, 1, 1), new(2028, 12, 31), [new(2027, 1, 1)]));

    [Fact]
    public void Given_three_dates_When_projecting_Then_payments_finish_at_the_exact_target()
    {
        var result = _forecast.Calculate(new(100m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15));

        Assert.False(result.NeededNow);
        Assert.Equal([new DateOnly(2027, 2, 1), new(2027, 3, 1), new(2027, 4, 1)],
            result.Payments.Select(x => x.Date));
        Assert.Equal([33.34m, 33.34m, 33.32m], result.Payments.Select(x => x.Amount));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public void Given_today_is_an_allocation_day_When_projecting_Then_only_a_recorded_payment_excludes_today(bool paid, int count)
    {
        var result = _forecast.Calculate(new(100m, 20m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 4), paid);

        Assert.Equal(count, result.Payments.Length);
        Assert.Equal(paid ? new DateOnly(2027, 2, 1) : new(2027, 1, 4), result.Payments[0].Date);
        Assert.Equal(80m, result.Payments.Sum(x => x.Amount));
    }

    [Fact]
    public void Given_a_future_start_after_allocation_day_When_projecting_Then_only_dates_from_start_through_deadline_count()
    {
        var result = _forecast.Calculate(new(90m, 0m, new(2027, 2, 2), new(2027, 4, 1)), new(2027, 1, 15), true);

        Assert.Equal([new DateOnly(2027, 3, 1), new(2027, 4, 1)], result.Payments.Select(x => x.Date));
        Assert.All(result.Payments, payment => Assert.Equal(45m, payment.Amount));
    }

    [Theory]
    [InlineData("2027-01-01", "2027-01-14")]
    [InlineData("2027-01-01", "2027-01-31")]
    [InlineData("2027-03-02", "2027-03-31")]
    public void Given_no_feasible_allocation_dates_When_projecting_Then_even_future_goals_need_the_full_remainder_now(
        string start, string deadline)
    {
        var result = _forecast.Calculate(new(100m, 25m, DateOnly.Parse(start), DateOnly.Parse(deadline)), new(2027, 1, 15));

        Assert.True(result.NeededNow);
        Assert.Equal(new ForecastPayment(new(2027, 1, 15), 75m), Assert.Single(result.Payments));
    }

    [Fact]
    public void Given_a_long_deadline_When_projecting_Then_the_schedule_is_not_truncated_to_24_months()
    {
        var result = _forecast.Calculate(new(360m, 0m, new(2027, 1, 1), new(2030, 1, 1)), new(2027, 1, 15));

        Assert.Equal(36, result.Payments.Length);
        Assert.All(result.Payments, payment => Assert.Equal(10m, payment.Amount));
        Assert.Equal(new DateOnly(2030, 1, 1), result.Payments[^1].Date);
    }

    [Fact]
    public void Given_a_penny_spread_over_many_dates_When_projecting_Then_no_payment_overfunds_the_goal()
    {
        var result = _forecast.Calculate(new(0.01m, 0m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15));

        Assert.Equal(new ForecastPayment(new(2027, 2, 1), 0.01m), Assert.Single(result.Payments));
    }

    [Fact]
    public void Given_a_funded_goal_When_projecting_Then_no_payments_or_warning_remain()
    {
        var result = _forecast.Calculate(new(100m, 100m, new(2027, 3, 2), new(2027, 3, 31)), new(2027, 1, 15));

        Assert.Empty(result.Payments);
        Assert.False(result.NeededNow);
    }

    [Fact]
    public void Given_an_invalid_goal_When_projecting_Then_it_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _forecast.Calculate(
            new(100m, 101m, new(2027, 1, 1), new(2027, 4, 1)), new(2027, 1, 15)));
        Assert.Throws<ArgumentNullException>(() => _forecast.Calculate(null!, new(2027, 1, 15)));
    }
}
