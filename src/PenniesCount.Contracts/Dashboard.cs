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
    HolidayCalendarResponse HolidayCalendar, SavingsGroupResponse[] Groups)
{
    public SavingsSummaryResponse[] Summaries { get; init; } = [];
    public SavingsPurposeSummaryResponse[] PurposeSummaries { get; init; } = [];
}

/// <summary>Category totals, or grand totals when Type is null; funded amounts include drawdowns.</summary>
public sealed record SavingsSummaryResponse(SavingsTerm? Type, decimal TargetAmount, decimal FundedAmount,
    decimal DrawnDownAmount, decimal AvailableAmount, decimal? RequiredMonthlyContribution);

/// <summary>Purpose-category totals; null Category means Uncategorised. Funded amounts include drawdowns.</summary>
public sealed record SavingsPurposeSummaryResponse(SavingsCategory? Category, decimal TargetAmount, decimal FundedAmount,
    decimal DrawnDownAmount, decimal AvailableAmount, decimal? RequiredMonthlyContribution);

public sealed record HolidayCalendarResponse(string Source, DateOnly? CoveredThrough,
    DateTimeOffset? LastSuccessfulRefreshUtc, bool IsStale, string? Warning);
