using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;
using PenniesCount.Contracts;
using PenniesCount.Core;

namespace PenniesCount.Api;

internal static class DashboardEndpoints
{
    public static void MapDashboard(this WebApplication app) =>
        app.MapGet("/api/dashboard", async (SavingsDbContext db, TimeProvider clock,
            IBankHolidayProvider holidayProvider, CancellationToken ct) =>
        {
            var snapshot = await BudgetSnapshot.LoadAsync(db, clock, holidayProvider, ct);
            var asOf = snapshot.AsOf;
            var schedule = snapshot.Schedule;
            var calculator = new BudgetCalculator(schedule);
            var nextAllocationDate = schedule.NextAllocationDate(asOf);
            var itemRows = snapshot.Items.Select(x => (Entity: x, Response: x.ToResponse(asOf))).ToArray();
            var items = itemRows.Select(x => x.Response).ToArray();
            var groups = Enum.GetValues<SavingsTerm>().Select(type => new SavingsGroupResponse(type,
                itemRows.Where(x => x.Response.Type == type).Select(row =>
                {
                    var item = row.Response;
                    var paidToday = row.Entity.HasPaymentOn(asOf);
                    var contribution = calculator.Calculate(
                        new(item.TargetAmount, item.AllocatedAmount, item.StartDate, item.TargetDate), asOf, paidToday);
                    var status = contribution.Status switch
                    {
                        ContributionStatus.Funded => ContributionState.Funded,
                        ContributionStatus.NotStarted => ContributionState.NotStarted,
                        ContributionStatus.Scheduled => ContributionState.Scheduled,
                        ContributionStatus.NeededNow => ContributionState.NeededNow,
                        ContributionStatus.Unavailable => ContributionState.Unavailable,
                        _ => throw new InvalidOperationException("Unknown contribution status.")
                    };
                    return new DashboardItemResponse(item, item.TargetAmount - item.AllocatedAmount,
                        contribution.RequiredAmount, contribution.RemainingPayments, status) { PaidToday = paidToday };
                }).ToArray())).ToArray();
            var savings = snapshot.Savings;
            var allocated = items.Sum(x => x.AvailableAmount);
            var contributions = groups.SelectMany(x => x.Items).Select(x => x.RequiredContribution).ToArray();
            decimal? requiredMonthlySavings = contributions.All(x => x.HasValue)
                ? contributions.Sum(x => x.GetValueOrDefault())
                : null;
            return new DashboardResponse(asOf, savings, allocated, savings - allocated,
                requiredMonthlySavings, nextAllocationDate, snapshot.Holidays, groups)
            {
                Summaries = Summarize(groups)
            };
        });

    internal static SavingsSummaryResponse[] Summarize(SavingsGroupResponse[] groups)
    {
        SavingsSummaryResponse Total(SavingsTerm? type, DashboardItemResponse[] rows) =>
            new(type, rows.Sum(x => x.Item.TargetAmount), rows.Sum(x => x.Item.AllocatedAmount),
                rows.Sum(x => x.Item.DrawnDownAmount), rows.Sum(x => x.Item.AvailableAmount),
                rows.All(x => x.RequiredContribution.HasValue) ? rows.Sum(x => x.RequiredContribution) : null);
        return [.. Enum.GetValues<SavingsTerm>().Select(type =>
            Total(type, groups.Where(x => x.Type == type).SelectMany(x => x.Items).ToArray())),
            Total(null, groups.SelectMany(x => x.Items).ToArray())];
    }
}
