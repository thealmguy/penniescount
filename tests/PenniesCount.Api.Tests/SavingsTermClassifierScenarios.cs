using System.Globalization;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsTermClassifierScenarios
{
    [Theory]
    [InlineData("2027-01-15", "2027-01-14", SavingsTerm.Past)]
    [InlineData("2027-01-15", "2027-01-15", SavingsTerm.Short)]
    [InlineData("2027-01-15", "2028-01-14", SavingsTerm.Short)]
    [InlineData("2027-01-15", "2028-01-15", SavingsTerm.Medium)]
    [InlineData("2027-01-15", "2028-01-16", SavingsTerm.Medium)]
    [InlineData("2027-01-15", "2032-01-14", SavingsTerm.Medium)]
    [InlineData("2027-01-15", "2032-01-15", SavingsTerm.Medium)]
    [InlineData("2027-01-15", "2032-01-16", SavingsTerm.Long)]
    [InlineData("2028-02-29", "2029-02-27", SavingsTerm.Short)]
    [InlineData("2028-02-29", "2029-02-28", SavingsTerm.Medium)]
    [InlineData("2028-02-29", "2033-02-28", SavingsTerm.Medium)]
    [InlineData("2028-02-29", "2033-03-01", SavingsTerm.Long)]
    [InlineData("9994-01-01", "9999-01-01", SavingsTerm.Medium)]
    [InlineData("9994-01-01", "9999-12-31", SavingsTerm.Long)]
    [InlineData("9995-01-01", "9999-12-31", SavingsTerm.Medium)]
    [InlineData("9999-01-01", "9999-12-31", SavingsTerm.Short)]
    [InlineData("9998-01-01", "9999-12-31", SavingsTerm.Medium)]
    [InlineData("9999-12-31", "9999-12-31", SavingsTerm.Short)]
    public void Given_a_calendar_boundary_When_classifying_Then_the_term_matches_the_target_date(
        string asOf, string target, SavingsTerm expected)
    {
        Assert.Equal(expected, SavingsTermClassifier.Classify(
            DateOnly.ParseExact(target, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }
}
