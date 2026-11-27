using Microsoft.AspNetCore.Components;
using PenniesCount.Web.Services;
using Microsoft.Identity.Web;

namespace PenniesCount.Web.Components;

public abstract class ApiPageBase : ComponentBase
{
    [Inject] protected BudgetClient Api { get; set; } = null!;
    [Inject] private ILogger<ApiPageBase> Logger { get; set; } = null!;
    [Inject] private IServiceProvider Services { get; set; } = null!;
    protected bool Busy { get; private set; }
    protected string? Error { get; private set; }

    protected void ClearError() => Error = null;

    protected async Task<bool> PerformAsync(Func<Task> operation)
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        try
        {
            await operation();
            return true;
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            Logger.LogWarning("API authentication requires user interaction.");
            Services.GetRequiredService<MicrosoftIdentityConsentAndConditionalAccessHandler>()
                .HandleException(exception);
            Error = "Sign in again to continue.";
        }
        catch (ApiException exception)
        {
            Logger.LogWarning("API returned an unsuccessful or invalid response.");
            Error = exception.Message;
        }
        catch (HttpRequestException)
        {
            Logger.LogWarning("API connection failed.");
            Error = "The API is unavailable. Start it and retry.";
        }
        catch (TaskCanceledException)
        {
            Logger.LogWarning("API request timed out.");
            Error = "The API took too long to respond. Retry.";
        }
        finally
        {
            Busy = false;
        }
        return false;
    }
}
