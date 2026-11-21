namespace PenniesCount.Contracts;

public enum ContributionState { Funded, NotStarted, Scheduled, NeededNow, Unavailable }

public sealed record DashboardItemResponse(SavingsItemResponse Item, decimal RemainingAmount,
    decimal? RequiredContribution, int? RemainingPayments, ContributionState Status)
{
    public bool PaidToday { get; init; }
}

public sealed record SavingsGroupResponse(SavingsTerm Type, DashboardItemResponse[] Items);

public sealed record DashboardResponse(DateOnly AsOfDate, decimal TotalSavings, decimal TotalAllocated,
    decimal UnallocatedBalance, decimal? RequiredMonthlySavings, DateOnly? NextAllocationDate,
    HolidayCalendarResponse HolidayCalendar, SavingsGroupResponse[] Groups);

public sealed record HolidayCalendarResponse(string Source, DateOnly? CoveredThrough,
    DateTimeOffset? LastSuccessfulRefreshUtc, bool IsStale, string? Warning);
