using PenniesCount.Api.Data;
using PenniesCount.Contracts;

namespace PenniesCount.Api.Tests;

public sealed class SavingsItemRuleScenarios
{
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);
    private const string Current = "AAAAAAAAAAE=";
    private const string Stale = "AAAAAAAAAAI=";

    private static SavingsItem Item(decimal target = 100m, decimal funded = 0m)
    {
        var item = new SavingsItem
        {
            Name = "Holiday", TargetAmount = target, StartDate = Today.AddDays(-10), TargetDate = Today.AddDays(90),
            Version = [0, 0, 0, 0, 0, 0, 0, 1]
        };
        if (funded != 0m) item.Record(LedgerEntryType.OpeningFunding, funded, Today, null, Now);
        return item;
    }

    private static LedgerRuleViolation Invalid(string field, string message, string rule) =>
        new(LedgerRuleKind.Invalid, field, message, rule);

    public static TheoryData<string, bool, LedgerRuleViolation?> WritableCases => new()
    {
        { Current, false, null },
        { Stale, false, LedgerRuleViolation.Conflict },
        { Current, true, LedgerRuleViolation.Removed },
        { Stale, true, LedgerRuleViolation.Conflict }
    };

    [Theory]
    [MemberData(nameof(WritableCases))]
    public void Given_a_version_and_removal_state_When_checking_writes_Then_stale_wins_over_removed(
        string version, bool removed, LedgerRuleViolation? expected)
    {
        var item = Item();
        item.IsRemoved = removed;
        Assert.Equal(expected, item.CheckWritable(version));
    }

    [Fact]
    public void Given_a_stale_or_removed_item_When_attempting_each_write_Then_nothing_is_recorded_or_changed()
    {
        foreach (var (version, removed, expected) in new[]
                 {
                     (Stale, false, LedgerRuleViolation.Conflict), (Current, true, LedgerRuleViolation.Removed)
                 })
        {
            var item = Item(funded: 50m);
            var spend = item.Record(LedgerEntryType.Drawdown, 10m, Today, null, Now);
            item.IsRemoved = removed;
            Assert.Equal(expected, item.RecordPayment(version, 1m, Today, Later, out var payment));
            Assert.Null(payment);
            Assert.Equal(expected, item.ReverseEntry(version, spend.Id, Later, out var reversal));
            Assert.Null(reversal);
            Assert.Equal(expected, item.RecordDrawdown(version, null, 1m, Today, null, Later, out var drawdown));
            Assert.Null(drawdown);
            Assert.Equal(expected, item.ReverseDrawdown(version, spend.Id, Later));
            Assert.Equal(expected, item.SetTotalFunding(version, 60m, Today, Later));
            Assert.Equal(expected, item.UpdateDetails(version, "Renamed", Today.AddDays(1), SavingsCategory.HouseAndGarden));
            Assert.Equal(expected, item.Remove(version));
            Assert.Equal(2, item.Entries.Count);
            Assert.Equal(("Holiday", Today.AddDays(90), (SavingsCategory?)null, removed),
                (item.Name, item.TargetDate, item.Category, item.IsRemoved));
        }
    }

    [Fact]
    public void Given_a_create_request_When_opening_an_item_Then_details_are_trimmed_and_funding_is_recorded()
    {
        var request = new CreateSavingsItemRequest
        {
            Name = "  New car  ", Category = SavingsCategory.Transport, TargetAmount = 500m,
            AllocatedAmount = 0.01m, StartDate = Today.AddDays(-1), TargetDate = Today.AddDays(30)
        };
        var item = SavingsItem.Open(request, Today, Now);
        Assert.Equal(("New car", SavingsCategory.Transport, 500m, Today.AddDays(-1), Today.AddDays(30), 0.01m),
            (item.Name, item.Category, item.TargetAmount, item.StartDate, item.TargetDate, item.AllocatedAmount));
        var opening = Assert.Single(item.Entries);
        Assert.Equal((item.Id, LedgerEntryType.OpeningFunding, 0.01m, Today, "Initial allocation", Now),
            (opening.SavingsItemId, opening.Type, opening.Amount, opening.EffectiveDate, opening.Description,
                opening.RecordedAtUtc));

        request.AllocatedAmount = 0m;
        Assert.Empty(SavingsItem.Open(request, Today, Now).Entries);
    }

    [Fact]
    public void Given_entry_dates_When_checking_payments_and_spending_Then_only_unset_or_future_dates_are_rejected()
    {
        var payment = Invalid("PaidOn", "Payment date is required and cannot be in the future.", "Ledger");
        var spending = Invalid("SpentOn", "Spending date is required and cannot be in the future.", "Drawdown date");
        Assert.Equal(payment, SavingsItem.CheckPaymentDate(DateOnly.MinValue, Today));
        Assert.Equal(payment, SavingsItem.CheckPaymentDate(Today.AddDays(1), Today));
        Assert.Null(SavingsItem.CheckPaymentDate(Today, Today));
        Assert.Null(SavingsItem.CheckPaymentDate(DateOnly.MinValue.AddDays(1), Today));
        Assert.Equal(spending, SavingsItem.CheckSpendingDate(DateOnly.MinValue, Today));
        Assert.Equal(spending, SavingsItem.CheckSpendingDate(Today.AddDays(1), Today));
        Assert.Null(SavingsItem.CheckSpendingDate(Today, Today));
        Assert.Null(SavingsItem.CheckSpendingDate(DateOnly.MinValue.AddDays(1), Today));
    }

    [Fact]
    public void Given_funding_below_target_When_paying_Then_payments_may_reach_but_not_exceed_the_target()
    {
        var item = Item(funded: 80m);
        Assert.Equal(Invalid("Amount", "Payments cannot take total funding above the target.", "Ledger"),
            item.RecordPayment(Current, 20.01m, Today, Later, out var rejected));
        Assert.Null(rejected);
        Assert.Single(item.Entries);

        Assert.Null(item.RecordPayment(Current, 20m, Today.AddDays(-1), Later, out var payment));
        Assert.Equal((item.Id, LedgerEntryType.AllocationPayment, 20m, Today.AddDays(-1), (string?)null, Later),
            (payment!.SavingsItemId, payment.Type, payment.Amount, payment.EffectiveDate, payment.Description,
                payment.RecordedAtUtc));
        Assert.Equal(100m, item.AllocatedAmount);
    }

    [Fact]
    public void Given_ledger_entries_When_reversing_Then_missing_reversal_and_already_reversed_entries_are_rejected()
    {
        var item = Item(funded: 50m);
        var spend = item.Record(LedgerEntryType.Drawdown, 10m, Today.AddDays(-2), null, Now);
        Assert.Equal(LedgerRuleViolation.NotFound, item.ReverseEntry(Current, Guid.NewGuid(), Later, out _));

        Assert.Null(item.ReverseEntry(Current, spend.Id, Later, out var reversal));
        Assert.Equal((item.Id, LedgerEntryType.Reversal, 10m, Today.AddDays(-2), (string?)null, Later, spend.Id),
            (reversal!.SavingsItemId, reversal.Type, reversal.Amount, reversal.EffectiveDate, reversal.Description,
                reversal.RecordedAtUtc, reversal.ReversesEntryId));
        Assert.Equal(0m, item.DrawnDownAmount);

        Assert.Equal(LedgerRuleViolation.AlreadyReversed, item.ReverseEntry(Current, spend.Id, Later, out _));
        Assert.Equal(Invalid("EntryId", "Reversal entries cannot be reversed.", "Ledger"),
            item.ReverseEntry(Current, reversal.Id, Later, out var none));
        Assert.Null(none);
        Assert.Equal(3, item.Entries.Count);
    }

    [Fact]
    public void Given_drawdowns_When_reversing_funding_Then_funding_may_fall_to_but_not_below_drawdowns()
    {
        var bounds = Invalid("EntryId",
            "Reversing this entry would take total funding below recorded drawdowns or above the target.", "Ledger");
        var item = Item(funded: 30m);
        var payment = item.Record(LedgerEntryType.AllocationPayment, 20m, Today, null, Now);
        var spend = item.Record(LedgerEntryType.Drawdown, 30.01m, Today, null, Now);
        Assert.Equal(bounds, item.ReverseEntry(Current, payment.Id, Later, out var rejected));
        Assert.Null(rejected);

        Assert.Null(item.ReverseEntry(Current, spend.Id, Later, out _));
        item.Record(LedgerEntryType.Drawdown, 30m, Today, null, Now);
        Assert.Null(item.ReverseEntry(Current, payment.Id, Later, out _));
        Assert.Equal((30m, 30m), (item.FundedAmount, item.DrawnDownAmount));
    }

    [Fact]
    public void Given_a_fully_drawn_item_When_reversing_a_drawdown_Then_released_spending_is_allowed()
    {
        var item = Item(funded: 100m);
        var spend = item.Record(LedgerEntryType.Drawdown, 100m, Today, null, Now);
        Assert.Null(item.ReverseEntry(Current, spend.Id, Later, out _));
        Assert.Equal((100m, 0m), (item.FundedAmount, item.DrawnDownAmount));
    }

    [Fact]
    public void Given_a_reduced_total_When_reversing_the_reduction_Then_funding_may_return_to_but_not_exceed_target()
    {
        var item = Item(funded: 100m);
        var reduction = item.Record(LedgerEntryType.FundingAdjustment, -20m, Today, null, Now);
        Assert.Null(item.ReverseEntry(Current, reduction.Id, Later, out _));
        Assert.Equal(100m, item.FundedAmount);

        var second = item.Record(LedgerEntryType.FundingAdjustment, -20m, Today, null, Now);
        item.Record(LedgerEntryType.AllocationPayment, 0.01m, Today, null, Now);
        Assert.Equal(Invalid("EntryId",
                "Reversing this entry would take total funding below recorded drawdowns or above the target.", "Ledger"),
            item.ReverseEntry(Current, second.Id, Later, out _));
    }

    [Fact]
    public void Given_available_funding_When_spending_Then_spending_may_use_but_not_exceed_funding()
    {
        var item = Item(funded: 50m);
        item.Record(LedgerEntryType.Drawdown, 20m, Today, null, Now);
        Assert.Equal(Invalid("Amount", "Drawdowns cannot exceed total funded allocation.", "Drawdown amount"),
            item.RecordDrawdown(Current, null, 30.01m, Today, null, Later, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(2, item.Entries.Count);

        Assert.Null(item.RecordDrawdown(Current, null, 30m, Today.AddDays(-3), "  Flights  ", Later, out var spend));
        Assert.Equal((item.Id, LedgerEntryType.Drawdown, 30m, Today.AddDays(-3), "Flights", Later, (Guid?)null),
            (spend!.SavingsItemId, spend.Type, spend.Amount, spend.EffectiveDate, spend.Description,
                spend.RecordedAtUtc, spend.ReplacesEntryId));
        Assert.Equal(50m, item.DrawnDownAmount);
        Assert.Null(item.RecordDrawdown(Current, null, 0m, Today, null, Later, out var blank));
        Assert.Null(blank!.Description);
    }

    [Fact]
    public void Given_an_existing_drawdown_When_replacing_it_Then_its_amount_is_released_and_it_is_reversed()
    {
        var item = Item(funded: 100m);
        var original = item.Record(LedgerEntryType.Drawdown, 100m, Today.AddDays(-5), null, Now);
        Assert.Equal(Invalid("Amount", "Drawdowns cannot exceed total funded allocation.", "Drawdown amount"),
            item.RecordDrawdown(Current, original.Id, 100.01m, Today, null, Later, out _));
        Assert.False(item.IsReversed(original));

        Assert.Null(item.RecordDrawdown(Current, original.Id, 100m, Today, "Corrected", Later, out var replacement));
        var reversal = item.Entries.Single(x => x.Type == LedgerEntryType.Reversal);
        Assert.Equal((100m, Today.AddDays(-5), (string?)null, Later, original.Id),
            (reversal.Amount, reversal.EffectiveDate, reversal.Description, reversal.RecordedAtUtc,
                reversal.ReversesEntryId));
        Assert.Equal((original.Id, "Corrected", 100m), (replacement!.ReplacesEntryId, replacement.Description,
            item.DrawnDownAmount));

        Assert.Equal(LedgerRuleViolation.AlreadyReversed,
            item.RecordDrawdown(Current, original.Id, 1m, Today, null, Later, out _));
        Assert.Equal(LedgerRuleViolation.AlreadyReversed, item.ReverseDrawdown(Current, original.Id, Later));
        Assert.Equal(4, item.Entries.Count);
    }

    [Fact]
    public void Given_a_non_drawdown_or_unknown_id_When_editing_or_reversing_spending_Then_it_is_not_found()
    {
        var item = Item();
        var opening = item.Record(LedgerEntryType.OpeningFunding, 50m, Today, null, Now);
        Assert.Null(item.FindDrawdown(opening.Id));
        Assert.Null(item.FindDrawdown(null));
        Assert.Equal(LedgerRuleViolation.NotFound,
            item.RecordDrawdown(Current, opening.Id, 1m, Today, null, Later, out var drawdown));
        Assert.Null(drawdown);
        Assert.Equal(LedgerRuleViolation.NotFound,
            item.RecordDrawdown(Current, Guid.NewGuid(), 1m, Today, null, Later, out _));
        Assert.Equal(LedgerRuleViolation.NotFound, item.ReverseDrawdown(Current, opening.Id, Later));
        Assert.Single(item.Entries);
    }

    [Fact]
    public void Given_a_drawdown_When_deleting_it_Then_a_reversal_is_recorded()
    {
        var item = Item(funded: 50m);
        var spend = item.Record(LedgerEntryType.Drawdown, 20m, Today.AddDays(-1), "Hotel", Now);
        Assert.Same(spend, item.FindDrawdown(spend.Id));
        Assert.Null(item.ReverseDrawdown(Current, spend.Id, Later));
        var reversal = item.Entries.Last();
        Assert.Equal((LedgerEntryType.Reversal, 20m, Today.AddDays(-1), (string?)null, Later, spend.Id),
            (reversal.Type, reversal.Amount, reversal.EffectiveDate, reversal.Description, reversal.RecordedAtUtc,
                reversal.ReversesEntryId));
        Assert.Equal(0m, item.DrawnDownAmount);
    }

    [Fact]
    public void Given_drawdowns_and_a_target_When_setting_total_funding_Then_it_must_stay_within_both()
    {
        var bounds = Invalid("AllocatedAmount",
            "Total funding must cover recorded drawdowns and cannot exceed the target.", "Item allocation");
        var item = Item(funded: 50m);
        item.Record(LedgerEntryType.Drawdown, 20m, Today, null, Now);
        Assert.Equal(bounds, item.SetTotalFunding(Current, 100.01m, Today, Later));
        Assert.Equal(bounds, item.SetTotalFunding(Current, 19.99m, Today, Later));
        Assert.Null(item.SetTotalFunding(Current, 50m, Today, Later));
        Assert.Equal(2, item.Entries.Count);

        Assert.Null(item.SetTotalFunding(Current, 100m, Today.AddDays(-1), Later));
        var increase = item.Entries.Last();
        Assert.Equal((LedgerEntryType.FundingAdjustment, 50m, Today.AddDays(-1), "Total funding updated", Later),
            (increase.Type, increase.Amount, increase.EffectiveDate, increase.Description, increase.RecordedAtUtc));
        Assert.Null(item.SetTotalFunding(Current, 20m, Today, Later));
        Assert.Equal((-80m, 20m), (item.Entries.Last().Amount, item.AllocatedAmount));
    }

    [Fact]
    public void Given_a_start_date_When_updating_details_Then_the_target_date_may_equal_but_not_precede_it()
    {
        var item = Item();
        Assert.Equal(Invalid("TargetDate", "Target date must be on or after the start date.", "Item target date"),
            item.UpdateDetails(Current, "Renamed", item.StartDate.AddDays(-1), SavingsCategory.HouseAndGarden));
        Assert.Equal(("Holiday", Today.AddDays(90), (SavingsCategory?)null),
            (item.Name, item.TargetDate, item.Category));

        Assert.Null(item.UpdateDetails(Current, "  Renamed  ", item.StartDate, SavingsCategory.HouseAndGarden));
        Assert.Equal(("Renamed", item.StartDate, (SavingsCategory?)SavingsCategory.HouseAndGarden),
            (item.Name, item.TargetDate, item.Category));
    }

    [Fact]
    public void Given_a_current_item_When_removing_it_Then_it_becomes_removed()
    {
        var item = Item();
        Assert.Null(item.Remove(Current));
        Assert.True(item.IsRemoved);
        Assert.Equal(LedgerRuleViolation.Removed, item.Remove(Current));
    }
}
