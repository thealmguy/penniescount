namespace PenniesCount.Contracts;

/// <summary>A category's projected funding in one calendar month.</summary>
public sealed record ForecastCategoryResponse(SavingsTerm Type, decimal Amount);

/// <summary>Projected payments for one month, including all category values and their total.</summary>
public sealed record ForecastMonthResponse(DateOnly Month, ForecastCategoryResponse[] Categories, decimal Total);

/// <summary>A goal with no eligible payment date before its deadline, requiring immediate funding.</summary>
public sealed record ForecastAttentionResponse(Guid ItemId, string Name, decimal Amount);

/// <summary>A read-only 24-month funding projection using the as-of date's goal categories.</summary>
public sealed record FutureLookResponse(DateOnly AsOfDate, HolidayCalendarResponse HolidayCalendar,
    ForecastMonthResponse[] Months, ForecastAttentionResponse[] NeededNow);
