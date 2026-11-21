namespace PenniesCount.Contracts;

public enum ContributionState { Funded, NotStarted, Scheduled, NeededNow }

public sealed record DashboardItemResponse(SavingsItemResponse Item, decimal RemainingAmount,
    decimal RequiredContribution, int RemainingPayments, ContributionState Status);

public sealed record SavingsGroupResponse(SavingsTerm Type, DashboardItemResponse[] Items);

public sealed record DashboardResponse(DateOnly AsOfDate, decimal TotalSavings, decimal TotalAllocated,
    decimal UnallocatedBalance, decimal RequiredMonthlySavings, SavingsGroupResponse[] Groups);
