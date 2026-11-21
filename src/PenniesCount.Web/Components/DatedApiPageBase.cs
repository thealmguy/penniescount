using Microsoft.AspNetCore.Components;

namespace PenniesCount.Web.Components;

public abstract class DatedApiPageBase : ApiPageBase, IAsyncDisposable
{
    [Inject] private TimeProvider Clock { get; set; } = null!;
    private readonly CancellationTokenSource _stopping = new();
    private PeriodicTimer? _timer;
    private Task? _watch;
    protected abstract DateOnly? AsOfDate { get; }
    protected abstract Task LoadAsync();
    protected override Task OnInitializedAsync() => LoadAsync();

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(30), Clock);
            _watch = WatchDateAsync(_timer);
        }
        return Task.CompletedTask;
    }

    private async Task WatchDateAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token))
            {
                await InvokeAsync(async () =>
                {
                    if (AsOfDate is { } date && DateOnly.FromDateTime(Clock.GetLocalNow().DateTime) != date)
                    {
                        await LoadAsync();
                        StateHasChanged();
                    }
                });
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    public virtual async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _timer?.Dispose();
        if (_watch is not null) await _watch;
        _stopping.Dispose();
    }
}
