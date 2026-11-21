using PenniesCount.Contracts;

namespace PenniesCount.Web.Services;

public sealed class BudgetClient(HttpClient http)
{
    public async Task<HealthResponse> GetHealthAsync() =>
        await http.GetFromJsonAsync<HealthResponse>("api/health")
        ?? throw new InvalidOperationException("The API returned an empty response.");
}
