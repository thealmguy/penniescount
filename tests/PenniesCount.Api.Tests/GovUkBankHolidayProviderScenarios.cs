using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PenniesCount.Api.Holidays;

namespace PenniesCount.Api.Tests;

public sealed class GovUkBankHolidayProviderScenarios
{
    private static readonly DateTimeOffset Now = new(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Feed = """
        {
          "england-and-wales": {
            "events": [
              { "title": "New Year's Day", "date": "2027-01-01" },
              { "title": "Christmas Day", "date": "2027-12-27" },
              { "title": "New Year's Day", "date": "2028-01-03" },
              { "title": "Christmas Day", "date": "2028-12-25" },
              { "title": "New Year's Day", "date": "2029-01-01" },
              { "title": "Christmas Day", "date": "2029-12-25" },
              { "title": "New Year's Day", "date": "2030-01-01" }
            ]
          }
        }
        """;

    [Fact]
    public async Task Given_a_valid_feed_When_loaded_Then_complete_contiguous_years_are_cached_for_restart()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.CachePath, "null");
        var clock = new FakeTimeProvider(Now);
        var requests = 0;
        var requestedPath = "";
        using var client = CreateClient((request, _) =>
        {
            requests++;
            requestedPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(Json(HttpStatusCode.OK, Feed));
        });
        var provider = CreateProvider(client, clock, directory.CachePath);

        var status = await GetStatusAsync(provider);

        Assert.False(status.IsStale);
        Assert.Null(status.Warning);
        Assert.Equal(Now, status.LastSuccessfulRefreshUtc);
        Assert.Equal(new DateOnly(2027, 1, 1), status.Calendar!.CoveredFrom);
        Assert.Equal(new DateOnly(2029, 12, 31), status.Calendar.CoveredThrough);
        Assert.Contains(new DateOnly(2027, 1, 1), status.Calendar.Holidays);
        Assert.Equal("/bank-holidays.json", requestedPath);
        Assert.Equal(1, requests);

        var offlineRequests = 0;
        using var offlineClient = CreateClient((_, _) =>
        {
            offlineRequests++;
            throw new HttpRequestException("offline");
        });
        var restarted = CreateProvider(offlineClient, clock, directory.CachePath);
        var cached = await GetStatusAsync(restarted);

        Assert.False(cached.IsStale);
        Assert.Equal(status.Calendar.CoveredFrom, cached.Calendar!.CoveredFrom);
        Assert.Equal(status.Calendar.CoveredThrough, cached.Calendar.CoveredThrough);
        Assert.Equal(status.Calendar.Holidays.Order(), cached.Calendar.Holidays.Order());
        Assert.Equal(0, offlineRequests);
    }

    [Fact]
    public async Task Given_a_future_dated_cache_When_loaded_Then_it_is_ignored_and_replaced_from_the_feed()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.CachePath, CacheEntry(Now.AddDays(1), Feed));
        var requests = 0;
        using var client = CreateClient((_, _) =>
        {
            requests++;
            return Task.FromResult(Json(HttpStatusCode.OK, Feed));
        });
        var provider = CreateProvider(client, new FakeTimeProvider(Now), directory.CachePath);

        var status = await GetStatusAsync(provider);

        Assert.False(status.IsStale);
        Assert.Equal(Now, status.LastSuccessfulRefreshUtc);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("""{"scotland":{"events":[{"title":"Holiday","date":"2027-01-01"}]}}""")]
    [InlineData("""{"england-and-wales":{"events":[]}}""")]
    [InlineData("""{"england-and-wales":{"events":[{"title":"Holiday","date":"2027-13-01"},{"title":"Christmas","date":"2027-12-25"}]}}""")]
    [InlineData("""{"england-and-wales":{"events":[{"title":" ","date":"2027-01-01"},{"title":"Christmas","date":"2027-12-25"}]}}""")]
    [InlineData("""{"england-and-wales":{"events":[{"title":"Holiday","date":"2027-02-01"}]}}""")]
    public async Task Given_invalid_or_incomplete_feed_When_loaded_Then_no_holiday_set_is_assumed(string content)
    {
        using var directory = new TemporaryDirectory();
        using var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, content)));
        var provider = CreateProvider(client, new FakeTimeProvider(Now), directory.CachePath);

        var status = await GetStatusAsync(provider);

        Assert.Null(status.Calendar);
        Assert.True(status.IsStale);
        Assert.NotNull(status.Warning);
        Assert.False(File.Exists(directory.CachePath));
    }

    [Fact]
    public async Task Given_a_stale_cache_and_an_offline_feed_When_refreshed_Then_the_last_good_dates_are_retained()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeTimeProvider(Now);
        using (var onlineClient = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Feed))))
        {
            var initial = CreateProvider(onlineClient, new FakeTimeProvider(Now.AddHours(-25)), directory.CachePath);
            Assert.NotNull((await GetStatusAsync(initial)).Calendar);
        }

        using var offlineClient = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var provider = CreateProvider(offlineClient, clock, directory.CachePath);

        var status = await GetStatusAsync(provider);

        Assert.NotNull(status.Calendar);
        Assert.Equal(Now.AddHours(-25), status.LastSuccessfulRefreshUtc);
        Assert.True(status.IsStale);
        Assert.Contains("could not be refreshed", status.Warning);
    }

    [Fact]
    public async Task Given_updated_official_dates_When_the_refresh_interval_expires_Then_the_new_calendar_replaces_the_old_one()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeTimeProvider(Now);
        var requests = 0;
        using var client = CreateClient((_, _) =>
        {
            requests++;
            var content = requests == 1
                ? Feed
                : Feed.Replace("\"date\": \"2027-12-27\"", "\"date\": \"2027-12-28\"", StringComparison.Ordinal);
            return Task.FromResult(Json(HttpStatusCode.OK, content));
        });
        var provider = CreateProvider(client, clock, directory.CachePath);

        var initial = await GetStatusAsync(provider);
        clock.Advance(TimeSpan.FromHours(24));
        var refreshed = await GetStatusAsync(provider);
        var initialCalendar = Assert.IsType<BankHolidayCalendar>(initial.Calendar);
        var refreshedCalendar = Assert.IsType<BankHolidayCalendar>(refreshed.Calendar);

        Assert.Contains(new DateOnly(2027, 12, 27), initialCalendar.Holidays);
        Assert.DoesNotContain(new DateOnly(2027, 12, 27), refreshedCalendar.Holidays);
        Assert.Contains(new DateOnly(2027, 12, 28), refreshedCalendar.Holidays);
        Assert.Equal(clock.GetUtcNow(), refreshed.LastSuccessfulRefreshUtc);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Given_a_failed_refresh_When_retried_before_and_after_the_backoff_Then_only_the_later_call_fetches_again()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeTimeProvider(Now);
        var requests = 0;
        using var client = CreateClient((_, _) =>
        {
            requests++;
            return Task.FromResult(requests == 1
                ? Json(HttpStatusCode.ServiceUnavailable, Feed)
                : Json(HttpStatusCode.OK, Feed));
        });
        var provider = CreateProvider(client, clock, directory.CachePath);

        Assert.True((await GetStatusAsync(provider)).IsStale);
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True((await GetStatusAsync(provider)).IsStale);
        Assert.Equal(1, requests);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await GetStatusAsync(provider)).IsStale);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Given_a_cancelled_caller_When_fetching_Then_cancellation_is_propagated_without_delaying_a_new_request()
    {
        using var directory = new TemporaryDirectory();
        var requests = 0;
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            if (++requests == 1) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, Feed);
        });
        var provider = CreateProvider(client, new FakeTimeProvider(Now), directory.CachePath);
        using var cancellation = new CancellationTokenSource();

        var request = provider.GetStatusAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.False((await GetStatusAsync(provider)).IsStale);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Given_a_timed_out_feed_When_loading_Then_it_returns_unavailable_without_waiting_for_the_handler()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeTimeProvider(Now);
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, Feed);
        });
        var provider = CreateProvider(client, clock, directory.CachePath);

        var request = provider.GetStatusAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(10));
        var status = await request;

        Assert.Null(status.Calendar);
        Assert.True(status.IsStale);
        Assert.Equal("The official holiday calendar is unavailable.", status.Warning);
    }

    [Fact]
    public async Task Given_cache_loaded_once_When_the_file_changes_during_the_process_Then_the_snapshot_is_not_replaced()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.CachePath, CacheEntry(Now, Feed));
        var clock = new FakeTimeProvider(Now);
        using var client = CreateClient((_, _) => throw new HttpRequestException("unexpected refresh"));
        var provider = CreateProvider(client, clock, directory.CachePath);
        var initial = await GetStatusAsync(provider);
        await File.WriteAllTextAsync(directory.CachePath,
            CacheEntry(Now, Feed.Replace("\"date\": \"2027-12-27\"", "\"date\": \"2027-12-28\"", StringComparison.Ordinal)));

        var unchanged = await GetStatusAsync(provider);
        var initialCalendar = Assert.IsType<BankHolidayCalendar>(initial.Calendar);
        var unchangedCalendar = Assert.IsType<BankHolidayCalendar>(unchanged.Calendar);

        Assert.Contains(new DateOnly(2027, 12, 27), initialCalendar.Holidays);
        Assert.Contains(new DateOnly(2027, 12, 27), unchangedCalendar.Holidays);
        Assert.DoesNotContain(new DateOnly(2027, 12, 28), unchangedCalendar.Holidays);
    }

    [Fact]
    public async Task Given_a_provider_that_has_served_a_status_When_requested_again_Then_the_gate_is_released()
    {
        using var directory = new TemporaryDirectory();
        using var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Feed)));
        var provider = CreateProvider(client, new FakeTimeProvider(Now), directory.CachePath);

        var first = await GetStatusAsync(provider);
        var second = await GetStatusAsync(provider);

        Assert.NotNull(first.Calendar);
        Assert.NotNull(second.Calendar);
    }

    [Fact]
    public async Task Given_no_cache_at_start_When_a_cache_file_is_added_later_Then_the_provider_keeps_its_initial_unavailable_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var clock = new FakeTimeProvider(Now);
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var provider = CreateProvider(client, clock, directory.CachePath);
        Assert.True((await GetStatusAsync(provider)).IsStale);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(directory.CachePath, CacheEntry(Now, Feed));

        var stillUnavailable = await GetStatusAsync(provider);

        Assert.Null(stillUnavailable.Calendar);
        Assert.True(stillUnavailable.IsStale);
    }

    [Fact]
    public async Task Given_a_cache_path_that_cannot_be_replaced_When_refreshing_Then_current_data_is_kept_and_temp_files_are_removed()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.CachePath);
        using var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Feed)));
        var provider = CreateProvider(client, new FakeTimeProvider(Now), directory.CachePath);

        var status = await GetStatusAsync(provider);

        Assert.NotNull(status.Calendar);
        Assert.False(status.IsStale);
        Assert.Equal("The holiday calendar was refreshed but could not be saved for offline use.", status.Warning);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task Given_a_cache_path_without_a_parent_When_saving_Then_it_throws_a_configuration_error()
    {
        using var client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Feed)));
        var provider = CreateProvider(client, new FakeTimeProvider(Now), "");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GetStatusAsync(provider));

        Assert.Equal("The bank holiday cache path must include a directory.", error.Message);
    }

    [Fact]
    public void Given_a_local_application_data_path_When_building_the_default_cache_path_Then_it_uses_the_app_folder_and_file_name()
    {
        var expected = Path.Combine("local-data", "PenniesCount", "bank-holidays.json");

        Assert.Equal(expected, GovUkBankHolidayProvider.GetDefaultCachePath("local-data"));
    }

    [Fact]
    public void Given_invalid_feed_shapes_When_parsing_Then_it_throws_specific_validation_errors()
    {
        var missingDivision = Assert.Throws<JsonException>(() => GovUkBankHolidayParser.Parse("""{"scotland":{"events":[]}}"""));
        var invalidEntry = Assert.Throws<JsonException>(() => GovUkBankHolidayParser.Parse(
            """{"england-and-wales":{"events":[{"title":"Holiday","date":"2027-13-01"},{"title":"Christmas","date":"2027-12-25"}]}}"""));
        var noCompleteYear = Assert.Throws<JsonException>(() => GovUkBankHolidayParser.Parse(
            """{"england-and-wales":{"events":[{"title":"Holiday","date":"2027-02-01"},{"title":"Christmas","date":"2027-12-25"}]}}"""));

        Assert.Equal("The GOV.UK England-and-Wales holiday list is missing or empty.", missingDivision.Message);
        Assert.Equal("A GOV.UK holiday entry has an invalid date or title.", invalidEntry.Message);
        Assert.Equal("The GOV.UK holiday list has no complete calendar year.", noCompleteYear.Message);
    }

    [Fact]
    public void Given_a_complete_year_one_feed_When_parsing_Then_coverage_starts_at_year_one()
    {
        var calendar = GovUkBankHolidayParser.Parse(
            """{"england-and-wales":{"events":[{"title":"New Year","date":"0001-01-01"},{"title":"Christmas","date":"0001-12-31"}]}}""");

        Assert.Equal(new DateOnly(1, 1, 1), calendar.CoveredFrom);
        Assert.Equal(new DateOnly(1, 12, 31), calendar.CoveredThrough);
    }

    private static GovUkBankHolidayProvider CreateProvider(HttpClient client, TimeProvider clock, string cachePath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["BankHolidays:CachePath"] = cachePath }).Build();
        return new GovUkBankHolidayProvider(client, configuration, clock,
            NullLogger<GovUkBankHolidayProvider>.Instance);
    }

    private static async Task<HolidayCalendarStatus> GetStatusAsync(GovUkBankHolidayProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await provider.GetStatusAsync(timeout.Token);
    }

    private static string CacheEntry(DateTimeOffset fetchedAtUtc, string content) =>
        JsonSerializer.Serialize(new { fetchedAtUtc, content }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new CallbackHandler(send)) { BaseAddress = new Uri("https://www.gov.uk/"), Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content) };

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PenniesCount-{Guid.NewGuid():N}");
        public string CachePath => System.IO.Path.Combine(Path, "bank-holidays.json");

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
