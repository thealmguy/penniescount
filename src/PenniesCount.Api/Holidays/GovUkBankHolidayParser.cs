using System.Globalization;
using System.Text.Json;

namespace PenniesCount.Api.Holidays;

internal static class GovUkBankHolidayParser
{
    public static BankHolidayCalendar Parse(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("england-and-wales", out var division)
            || division.ValueKind != JsonValueKind.Object
            || !division.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Array
            || events.GetArrayLength() == 0)
            throw new JsonException("The GOV.UK England-and-Wales holiday list is missing or empty.");

        var dates = new HashSet<DateOnly>();
        foreach (var holiday in events.EnumerateArray())
        {
            if (holiday.ValueKind != JsonValueKind.Object
                || !holiday.TryGetProperty("date", out var dateElement)
                || dateElement.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !holiday.TryGetProperty("title", out var titleElement)
                || titleElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(titleElement.GetString()))
                throw new JsonException("A GOV.UK holiday entry has an invalid date or title.");

            dates.Add(date);
        }

        var completeYears = dates.GroupBy(x => x.Year)
            .Where(year => year.Any(x => x.Month == 1) && year.Any(x => x.Month == 12))
            .Select(year => year.Key).ToHashSet();
        if (completeYears.Count == 0)
            throw new JsonException("The GOV.UK holiday list has no complete calendar year.");

        var yearsInDescendingOrder = completeYears.OrderDescending().ToArray();
        var coveredThroughYear = yearsInDescendingOrder[0];
        var coveredFromYear = coveredThroughYear;
        for (var index = 1; index < yearsInDescendingOrder.Length
             && yearsInDescendingOrder[index] == coveredFromYear - 1; index++)
            coveredFromYear = yearsInDescendingOrder[index];

        return new BankHolidayCalendar(dates, new(coveredFromYear, 1, 1), new(coveredThroughYear, 12, 31));
    }
}
