using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsLedgerEntityScenarios
{
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Given_funding_and_drawdowns_When_entries_are_reversed_Then_only_effective_entries_count()
    {
        var item = new SavingsItem { TargetAmount = 100m, TargetDate = Today.AddDays(90) };
        var opening = item.Record(LedgerEntryType.OpeningFunding, 80m, Today, null, Now);
        var adjustment = item.Record(LedgerEntryType.FundingAdjustment, -5m, Today, null, Now);
        var firstSpend = item.Record(LedgerEntryType.Drawdown, 20m, Today, "Initial spend", Now);
        var secondSpend = item.Record(LedgerEntryType.Drawdown, 15m, Today, null, Now);
        var payment = item.Record(LedgerEntryType.AllocationPayment, 15m, Today, null, Now);
        Assert.Equal((90m, 35m), (item.AllocatedAmount, item.DrawnDownAmount));
        Assert.Equal(55m, item.ToResponse(Today).AvailableAmount);
        Assert.True(item.HasPaymentOn(Today));
        Assert.False(item.HasPaymentOn(Today.AddDays(-1)));

        var paymentReversal = item.Record(LedgerEntryType.Reversal, 15m, Today, null, Now, reverses: payment.Id);
        var spendReversal = item.Record(LedgerEntryType.Reversal, 20m, Today, null, Now, reverses: firstSpend.Id);
        var replacement = item.Record(LedgerEntryType.Drawdown, 10m, Today, "Replacement", Now, replaces: firstSpend.Id);

        Assert.True(item.IsReversed(payment));
        Assert.True(item.IsReversed(firstSpend));
        Assert.False(item.IsReversed(replacement));
        Assert.False(item.HasPaymentOn(Today));
        Assert.Equal([opening, adjustment, secondSpend, replacement], item.EffectiveEntries);
        Assert.Equal((75m, 25m, 50m), (item.AllocatedAmount, item.DrawnDownAmount, item.ToResponse(Today).AvailableAmount));
        Assert.Equal((0m, 0m), (paymentReversal.FundingEffect, spendReversal.DrawdownEffect));
        Assert.Equal((item.Id, LedgerEntryType.Drawdown, 10m, Today, "Replacement", Now, firstSpend.Id),
            (replacement.SavingsItemId, replacement.Type, replacement.Amount, replacement.EffectiveDate,
                replacement.Description, replacement.RecordedAtUtc, replacement.ReplacesEntryId));
    }

    [Fact]
    public void Given_entries_with_tied_dates_When_reading_history_Then_time_and_id_break_the_ties()
    {
        var item = new SavingsItem();
        var laterDate = item.Record(LedgerEntryType.AllocationPayment, 1m, Today.AddDays(1), null, Now);
        var laterTime = item.Record(LedgerEntryType.AllocationPayment, 1m, Today, null, Now.AddMinutes(1));
        var higherId = item.Record(LedgerEntryType.AllocationPayment, 1m, Today, null, Now);
        higherId.Id = new Guid("00000000-0000-0000-0000-000000000002");
        var lowerId = item.Record(LedgerEntryType.AllocationPayment, 1m, Today, null, Now);
        lowerId.Id = new Guid("00000000-0000-0000-0000-000000000001");

        Assert.Equal([lowerId, higherId, laterTime, laterDate], item.History);
    }
}
