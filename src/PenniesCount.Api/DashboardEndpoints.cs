using System.Data;
using Microsoft.EntityFrameworkCore;
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
            var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var holidayStatus = await holidayProvider.GetStatusAsync(ct);
            var schedule = holidayStatus.Calendar is { } calendar
                ? new WorkingDaySchedule(calendar.CoveredFrom, calendar.CoveredThrough, calendar.Holidays)
                : WorkingDaySchedule.WeekdaysOnly;
            var calculator = new BudgetCalculator(schedule);
            var nextAllocationDate = schedule.NextAllocationDate(asOf);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var balances = await db.Accounts.AsNoTracking().Select(x => x.TotalFunds).ToListAsync(ct);
            var storedItems = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
                .Where(x => !x.IsRemoved).OrderBy(x => x.TargetDate)
                .ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            var itemRows = storedItems.Select(x => (Entity: x, Response: x.ToResponse(asOf))).ToArray();
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
            var savings = balances.Sum();
            var allocated = items.Sum(x => x.AvailableAmount);
            var contributions = groups.SelectMany(x => x.Items).Select(x => x.RequiredContribution).ToArray();
            decimal? requiredMonthlySavings = contributions.All(x => x.HasValue)
                ? contributions.Sum(x => x.GetValueOrDefault())
                : null;
            var holidayResponse = new HolidayCalendarResponse(HolidayCalendarStatus.Source,
                holidayStatus.Calendar?.CoveredThrough, holidayStatus.LastSuccessfulRefreshUtc,
                holidayStatus.IsStale, holidayStatus.Warning);
            return new DashboardResponse(asOf, savings, allocated, savings - allocated,
                requiredMonthlySavings, nextAllocationDate, holidayResponse, groups);
        });
}
