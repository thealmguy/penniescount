using System.Data;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;
using PenniesCount.Contracts;
using PenniesCount.Core;

namespace PenniesCount.Api;

internal sealed record BudgetSnapshot(DateOnly AsOf, decimal Savings, SavingsItem[] Items,
    WorkingDaySchedule Schedule, HolidayCalendarResponse Holidays)
{
    public static async Task<BudgetSnapshot> LoadAsync(SavingsDbContext db, TimeProvider clock,
        IBankHolidayProvider holidayProvider, CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var holidayStatus = await holidayProvider.GetStatusAsync(ct);
        var schedule = holidayStatus.Calendar is { } calendar
            ? new WorkingDaySchedule(calendar.CoveredFrom, calendar.CoveredThrough, calendar.Holidays)
            : WorkingDaySchedule.WeekdaysOnly;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var balances = await db.Accounts.AsNoTracking().Select(x => x.TotalFunds).ToListAsync(ct);
        var items = await db.SavingsItems.AsNoTracking().Include(x => x.Entries)
            .Where(x => !x.IsRemoved).OrderBy(x => x.TargetDate)
            .ThenBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(asOf, balances.Sum(), items, schedule, new(HolidayCalendarStatus.Source,
            holidayStatus.Calendar?.CoveredThrough, holidayStatus.LastSuccessfulRefreshUtc,
            holidayStatus.IsStale, holidayStatus.Warning));
    }
}
