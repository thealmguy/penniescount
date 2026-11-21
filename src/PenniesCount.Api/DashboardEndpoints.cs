using System.Data;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Contracts;
using PenniesCount.Core;

namespace PenniesCount.Api;

internal static class DashboardEndpoints
{
    public static void MapDashboard(this WebApplication app) =>
        app.MapGet("/api/dashboard", async (SavingsDbContext db, TimeProvider clock,
            BudgetCalculator calculator, CancellationToken ct) =>
        {
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var balances = await db.Accounts.AsNoTracking().Select(x => x.TotalFunds).ToListAsync(ct);
            var storedItems = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .Where(x => !x.IsRemoved).OrderBy(x => x.TargetDate)
                .ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            var items = storedItems.Select(x => x.ToResponse(asOf)).ToArray();
            var groups = Enum.GetValues<SavingsTerm>().Select(type => new SavingsGroupResponse(type,
                items.Where(item => item.Type == type).Select(item =>
                {
                    var contribution = calculator.Calculate(
                        new(item.TargetAmount, item.AllocatedAmount, item.StartDate, item.TargetDate), asOf);
                    var status = contribution.Status switch
                    {
                        ContributionStatus.Funded => ContributionState.Funded,
                        ContributionStatus.NotStarted => ContributionState.NotStarted,
                        ContributionStatus.Scheduled => ContributionState.Scheduled,
                        ContributionStatus.NeededNow => ContributionState.NeededNow,
                        _ => throw new InvalidOperationException("Unknown contribution status.")
                    };
                    return new DashboardItemResponse(item, item.TargetAmount - item.AllocatedAmount,
                        contribution.RequiredAmount, contribution.RemainingPayments, status);
                }).ToArray())).ToArray();
            var savings = balances.Sum();
            var allocated = items.Sum(x => x.AvailableAmount);
            return new DashboardResponse(asOf, savings, allocated, savings - allocated,
                groups.SelectMany(x => x.Items).Sum(x => x.RequiredContribution), groups);
        });
}
