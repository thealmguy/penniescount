using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PenniesCount.Contracts;
using PenniesCount.Web.Components.Pages;
using PenniesCount.Web.Services;

namespace PenniesCount.Web.Tests;

public sealed class DrawdownPageScenarios
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Given_available_funds_When_recording_spending_Then_the_api_date_and_version_are_used_and_errors_keep_the_edit(bool fail)
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler { Fail = fail };
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.Equal("£7,000.00", page.Find("#item-available").TextContent));
        Assert.Contains("£10,000.00", page.Find("#item-funded").TextContent);
        page.Find("#add-drawdown").Click();
        Assert.Equal("2027-01-15", page.Find("#spent-on").GetAttribute("value"));
        Assert.Equal("7000.00", page.Find("#drawdown-amount").GetAttribute("value"));
        page.Find("#drawdown-amount").Change("2000");
        page.Find("#drawdown-description").Change("Hotel");
        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2000m, handler.Saved?.Amount);
            Assert.Equal("Hotel", handler.Saved?.Description);
            Assert.Equal("AAAAAAAAAAA=", handler.Saved?.Version);
            Assert.Equal(new DateOnly(2027, 1, 15), handler.Saved?.SpentOn);
            if (fail)
            {
                Assert.Contains("Reload and retry", page.Find("[role=alert]").TextContent);
                Assert.Equal("2000", page.Find("#drawdown-amount").GetAttribute("value"));
            }
            else
            {
                Assert.Empty(page.FindAll("form"));
                Assert.Equal("£5,000.00", page.Find("#item-available").TextContent);
                Assert.Equal("£10,000.00", page.Find("#item-funded").TextContent);
            }
        });
        if (fail)
        {
            page.FindAll("button").Single(x => x.TextContent == "Cancel").Click();
            page.Find("#add-drawdown").Click();
            Assert.Empty(page.FindAll("[role=alert]"));
            Assert.Equal("7000.00", page.Find("#drawdown-amount").GetAttribute("value"));
        }
    }

    [Fact]
    public void Given_a_drawdown_When_corrected_or_reversed_Then_correction_uses_the_record_and_reversal_requires_confirmation()
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".edit-drawdown")));
        Assert.Equal(2, page.FindAll(".reverse-entry").Count);
        Assert.Contains("Inferred from earlier totals", page.Find(".ledger-entry").TextContent);
        page.Find(".edit-drawdown").Click();
        Assert.Equal("3000", page.Find("#drawdown-amount").GetAttribute("value"));
        Assert.Equal("Flights", page.Find("#drawdown-description").GetAttribute("value"));
        Assert.Contains("reverses the original drawdown", page.Markup);
        page.Find("#drawdown-amount").Change("2500");
        page.Find("form").Submit();
        page.WaitForAssertion(() =>
        {
            Assert.Equal(HttpMethod.Put, handler.Method);
            Assert.Equal(handler.Record.Id, handler.ChildId);
            Assert.Empty(page.FindAll("form"));
        });
        page.FindAll(".reverse-entry").Last().Click();
        Assert.Null(handler.ReversedVersion);
        Assert.Contains("original stays visible", page.Markup);
        page.Find("#confirm-action").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Equal("AAAAAAAAAAE=", handler.ReversedVersion);
            Assert.Equal(handler.Record.Id, handler.ChildId);
            Assert.Empty(page.FindAll(".edit-drawdown"));
            Assert.Single(page.FindAll(".reverse-entry"));
            Assert.Equal(3, page.FindAll(".ledger-entry").Count);
            Assert.Contains("Reverses an earlier entry", page.Markup);
            Assert.Contains("Reversed", page.FindAll(".ledger-entry")[1].TextContent);
            Assert.Equal("£10,000.00", page.Find("#item-available").TextContent);
        });
    }

    [Fact]
    public void Given_an_underfunded_item_When_recording_a_payment_Then_the_api_date_and_version_are_used()
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.False(page.Find("#add-payment").HasAttribute("disabled")));
        page.Find("#add-payment").Click();
        Assert.True(page.Find("#add-drawdown").HasAttribute("disabled"));
        Assert.Equal("2027-01-15", page.Find("#paid-on").GetAttribute("value"));
        page.Find("#payment-amount").Change("2000");
        page.Find("form").Submit();
        page.WaitForAssertion(() =>
        {
            Assert.Equal(2000m, handler.Payment?.Amount);
            Assert.Equal(new DateOnly(2027, 1, 15), handler.Payment?.PaidOn);
            Assert.Equal("AAAAAAAAAAA=", handler.Payment?.Version);
            Assert.Empty(page.FindAll("form"));
            Assert.Equal("£12,000.00", page.Find("#item-funded").TextContent);
            Assert.True(page.Find("#add-payment").HasAttribute("disabled"));
            Assert.Contains("Allocation payment", page.Markup);
        });
    }

    [Fact]
    public void Given_an_item_When_removal_is_confirmed_Then_its_history_becomes_read_only()
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("#remove-item")));
        page.Find("#remove-item").Click();
        Assert.False(handler.Item.IsRemoved);
        Assert.Contains("cannot be undone", page.Markup);
        page.Find("#confirm-action").Click();
        page.WaitForAssertion(() =>
        {
            Assert.True(handler.Item.IsRemoved);
            Assert.Empty(page.FindAll("#add-drawdown, #add-payment, #remove-item, .edit-drawdown, .reverse-entry"));
            Assert.Contains("Flights", page.Markup);
            Assert.Contains("read-only", page.Markup);
        });
        var removed = context.Render<RemovedSavingsItems>();
        removed.WaitForAssertion(() => Assert.Contains(handler.Item.Id.ToString(), removed.Find("li a").GetAttribute("href")));
    }

    [Fact]
    public async Task Given_a_pending_drawdown_When_cancel_is_attempted_Then_the_form_stays_open_and_duplicate_writes_are_disabled()
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler
        {
            Pending = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("#add-drawdown")));
        page.Find("#add-drawdown").Click();
        var submission = page.Find("form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find("fieldset").HasAttribute("disabled")));
        page.FindAll("button").Single(x => x.TextContent == "Cancel").Click();
        Assert.Single(page.FindAll("form"));
        Assert.True(page.Find("#add-drawdown").HasAttribute("disabled"));
        handler.Pending.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(handler.Record) });
        await submission;
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void Given_a_zero_drawdown_When_submitted_Then_no_write_occurs()
    {
        using var context = new BunitContext();
        var handler = new DrawdownHandler();
        context.Services.AddSingleton(new BudgetClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var page = context.Render<SavingsItemDetails>(p => p.Add(x => x.Id, handler.Item.Id));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("#add-drawdown")));
        page.Find("#add-drawdown").Click();
        page.Find("#drawdown-amount").Change("0");
        page.Find("form").Submit();
        Assert.Null(handler.Saved);
        Assert.NotEmpty(page.Find(".validation-errors").TextContent);
    }

    private sealed class DrawdownHandler : HttpMessageHandler
    {
        public SavingsItemResponse Item { get; private set; } = new(Guid.NewGuid(), "Holiday example", SavingsTerm.Short,
            12000m, 10000m, new(2027, 1, 1), new(2027, 4, 1), "AAAAAAAAAAA=")
        { DrawnDownAmount = 3000m, AvailableAmount = 7000.00m };
        public SavingsDrawdownResponse Record { get; } = new(Guid.NewGuid(), 3000m, new(2027, 1, 1),
            "Flights", new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero));
        private List<LedgerEntryResponse>? _entries;
        public bool Fail { get; init; }
        public TaskCompletionSource<HttpResponseMessage>? Pending { get; init; }
        public SaveDrawdownRequest? Saved { get; private set; }
        public RecordPaymentRequest? Payment { get; private set; }
        public string? ReversedVersion { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Guid? ChildId { get; private set; }

        private List<LedgerEntryResponse> Entries => _entries ??=
        [
            new(Guid.NewGuid(), LedgerEntryType.OpeningFunding, 10000m, new(2027, 1, 1), "Opening balance",
                new(2027, 1, 1, 9, 0, 0, TimeSpan.Zero), true, null, null, false),
            new(Record.Id, LedgerEntryType.Drawdown, Record.Amount, Record.SpentOn, Record.Description,
                Record.RecordedAtUtc, false, null, null, false)
        ];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri!.Query == "?removed=true")
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new SavingsItemsResponse(new(2027, 1, 15), [Item])) };
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SavingsLedgerResponse(new(2027, 1, 15), Item, [.. Entries]))
                };
            }
            Method = request.Method;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/removal"))
            {
                Item = Item with { IsRemoved = true };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Item) };
            }
            if (path.EndsWith("/reversal"))
            {
                ReversedVersion = (await request.Content!.ReadFromJsonAsync<ItemVersionRequest>(ct))!.Version;
                ChildId = Guid.Parse(request.RequestUri.Segments[^2].TrimEnd('/'));
                var original = Entries.Single(x => x.Id == ChildId);
                Entries[Entries.IndexOf(original)] = original with { IsReversed = true };
                var reversal = new LedgerEntryResponse(Guid.NewGuid(), LedgerEntryType.Reversal, original.Amount,
                    original.EffectiveDate, null, original.RecordedAtUtc, false, original.Id, null, false);
                Entries.Add(reversal);
                Item = Item with { DrawnDownAmount = 0m, AvailableAmount = 10000m };
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(reversal) };
            }
            if (path.EndsWith("/payments"))
            {
                Payment = await request.Content!.ReadFromJsonAsync<RecordPaymentRequest>(ct);
                var payment = new LedgerEntryResponse(Guid.NewGuid(), LedgerEntryType.AllocationPayment, Payment!.Amount,
                    Payment.PaidOn, null, new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero), false, null, null, false);
                Entries.Add(payment);
                Item = Item with { AllocatedAmount = 12000m, AvailableAmount = 9000m, Version = "AAAAAAAAAAE=" };
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(payment) };
            }
            Saved = await request.Content!.ReadFromJsonAsync<SaveDrawdownRequest>(ct);
            if (request.Method == HttpMethod.Put) ChildId = Guid.Parse(request.RequestUri.Segments.Last());
            if (Fail)
                return new(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { title = "Reload and retry" }, mediaType: new("application/problem+json"))
                };
            if (Pending is not null) return await Pending.Task;
            Item = Item with { DrawnDownAmount = 5000m, AvailableAmount = 5000m, Version = "AAAAAAAAAAE=" };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Record) };
        }
    }
}
