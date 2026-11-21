namespace PenniesCount.Api.Holidays;

public sealed record BankHolidayCalendar(
    IReadOnlySet<DateOnly> Holidays,
    DateOnly CoveredFrom,
    DateOnly CoveredThrough);

public sealed record HolidayCalendarStatus(
    BankHolidayCalendar? Calendar,
    DateTimeOffset? LastSuccessfulRefreshUtc,
    bool IsStale,
    string? Warning)
{
    public const string Source = "GOV.UK bank holidays (England and Wales)";
}
