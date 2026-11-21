namespace PenniesCount.Api.Holidays;

public interface IBankHolidayProvider
{
    Task<HolidayCalendarStatus> GetStatusAsync(CancellationToken cancellationToken);
}
