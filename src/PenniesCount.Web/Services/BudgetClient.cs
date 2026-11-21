using PenniesCount.Contracts;

namespace PenniesCount.Web.Services;

public sealed class BudgetClient(HttpClient http)
{
    public Task<HealthResponse> GetHealthAsync() => SendAsync<HealthResponse>(HttpMethod.Get, "api/health");
    public Task<AccountResponse[]> GetAccountsAsync() => SendAsync<AccountResponse[]>(HttpMethod.Get, "api/accounts");
    public Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request) =>
        SendAsync<AccountResponse>(HttpMethod.Post, "api/accounts", request);
    public Task<AccountResponse> UpdateBalanceAsync(Guid id, UpdateBalanceRequest request) =>
        SendAsync<AccountResponse>(HttpMethod.Put, $"api/accounts/{id}/balance", request);
    public Task<SavingsItemsResponse> GetItemsAsync() => SendAsync<SavingsItemsResponse>(HttpMethod.Get, "api/savings-items");
    public Task<SavingsItemsResponse> GetRemovedItemsAsync() =>
        SendAsync<SavingsItemsResponse>(HttpMethod.Get, "api/savings-items?removed=true");
    public Task<SavingsLedgerResponse> GetLedgerAsync(Guid id) =>
        SendAsync<SavingsLedgerResponse>(HttpMethod.Get, $"api/savings-items/{id}/ledger");
    public Task<LedgerEntryResponse> RecordPaymentAsync(Guid id, RecordPaymentRequest request) =>
        SendAsync<LedgerEntryResponse>(HttpMethod.Post, $"api/savings-items/{id}/payments", request);
    public Task<LedgerEntryResponse> ReverseEntryAsync(Guid id, Guid entryId, ItemVersionRequest request) =>
        SendAsync<LedgerEntryResponse>(HttpMethod.Post, $"api/savings-items/{id}/ledger/{entryId}/reversal", request);
    public Task<SavingsDrawdownResponse> SaveDrawdownAsync(Guid id, Guid? drawdownId, SaveDrawdownRequest request) =>
        SendAsync<SavingsDrawdownResponse>(drawdownId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            $"api/savings-items/{id}/drawdowns" + (drawdownId.HasValue ? $"/{drawdownId}" : ""), request);
    public Task<SavingsItemResponse> RemoveItemAsync(Guid id, ItemVersionRequest request) =>
        SendAsync<SavingsItemResponse>(HttpMethod.Put, $"api/savings-items/{id}/removal", request);
    public Task<DashboardResponse> GetDashboardAsync() => SendAsync<DashboardResponse>(HttpMethod.Get, "api/dashboard");
    public Task<SavingsItemResponse> CreateItemAsync(CreateSavingsItemRequest request) =>
        SendAsync<SavingsItemResponse>(HttpMethod.Post, "api/savings-items", request);
    public Task<SavingsItemResponse> UpdateAllocationAsync(Guid id, UpdateAllocationRequest request) =>
        SendAsync<SavingsItemResponse>(HttpMethod.Put, $"api/savings-items/{id}/allocation", request);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        using var response = await http.SendAsync(request);
        await CheckResponseAsync(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new ApiException("The API returned an empty response.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ApiException("The API returned an invalid response.");
        }
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        try
        {
            var problem = response.Content.Headers.ContentType?.MediaType == "application/problem+json"
                ? await response.Content.ReadFromJsonAsync<ApiProblem>() : null;
            var message = problem?.Errors?.Count > 0
                ? string.Join(" ", problem.Errors.Values.SelectMany(x => x))
                : problem?.Title ?? $"API request failed ({(int)response.StatusCode}). Retry or reload the page.";
            throw new ApiException(message);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ApiException("The API returned an invalid response.");
        }
    }

    private sealed record ApiProblem(string? Title, Dictionary<string, string[]>? Errors);
}

public sealed class ApiException(string message) : Exception(message);
