using System.Text.Json;

namespace PenniesCount.Api.Holidays;

public sealed class GovUkBankHolidayProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<GovUkBankHolidayProvider> logger) : IBankHolidayProvider
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _cachePath = configuration["BankHolidays:CachePath"]
        ?? GetDefaultCachePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private BankHolidayCalendar? _calendar;
    private DateTimeOffset? _lastSuccessfulRefreshUtc;
    private DateTimeOffset? _lastAttemptUtc;
    private string? _warning;
    private bool _refreshFailed;
    private bool _cacheLoaded;

    public async Task<HolidayCalendarStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_cacheLoaded)
                await LoadCacheAsync(cancellationToken);

            var now = clock.GetUtcNow();
            var refreshDue = _lastSuccessfulRefreshUtc is null
                || now - _lastSuccessfulRefreshUtc >= RefreshInterval;
            var retryDue = _lastAttemptUtc is null || now - _lastAttemptUtc >= RetryInterval;
            if (refreshDue && retryDue)
                await RefreshAsync(cancellationToken);

            var stale = _calendar is null || _refreshFailed || _lastSuccessfulRefreshUtc is null;
            return new(_calendar, _lastSuccessfulRefreshUtc, stale, _warning);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_cachePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_cachePath, cancellationToken);
                var cached = JsonSerializer.Deserialize<BankHolidayCache>(content, JsonOptions);
                if (cached is null || cached.FetchedAtUtc == default
                    || cached.FetchedAtUtc > clock.GetUtcNow() || string.IsNullOrWhiteSpace(cached.Content))
                    throw new JsonException();
                var calendar = GovUkBankHolidayParser.Parse(cached.Content);
                _calendar = calendar;
                _lastSuccessfulRefreshUtc = cached.FetchedAtUtc;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(exception, "Ignoring an invalid or unreadable bank holiday cache.");
            }
        }

        _cacheLoaded = true;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var previousAttemptUtc = _lastAttemptUtc;
        _lastAttemptUtc = clock.GetUtcNow();
        using var timeout = new CancellationTokenSource(RequestTimeout, clock);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var response = await httpClient.GetAsync(
                "bank-holidays.json", HttpCompletionOption.ResponseHeadersRead, linkedCancellation.Token);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(linkedCancellation.Token);
            var calendar = GovUkBankHolidayParser.Parse(content);
            var fetchedAtUtc = clock.GetUtcNow();
            _calendar = calendar;
            _lastSuccessfulRefreshUtc = fetchedAtUtc;
            _refreshFailed = false;
            _warning = null;

            try
            {
                await SaveCacheAsync(new(fetchedAtUtc, content), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _warning = "The holiday calendar was refreshed but could not be saved for offline use.";
                logger.LogWarning(exception, "The refreshed bank holiday data could not be cached.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _lastAttemptUtc = previousAttemptUtc;
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            _refreshFailed = true;
            _warning = _calendar is null
                ? "The official holiday calendar is unavailable."
                : "The official holiday calendar could not be refreshed; cached dates may be out of date.";
            logger.LogWarning(exception, "Unable to refresh the GOV.UK bank holiday calendar.");
        }
    }

    private async Task SaveCacheAsync(BankHolidayCache cached, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new InvalidOperationException("The bank holiday cache path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var content = JsonSerializer.Serialize(cached, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Unable to remove a temporary bank holiday cache file.");
                }
            }
        }
    }

    private sealed record BankHolidayCache(DateTimeOffset FetchedAtUtc, string Content);

    internal static string GetDefaultCachePath(string localApplicationDataPath) =>
        Path.Combine(localApplicationDataPath, "PenniesCount", "bank-holidays.json");
}
