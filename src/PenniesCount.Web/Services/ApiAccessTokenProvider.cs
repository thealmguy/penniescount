using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;

namespace PenniesCount.Web.Services;

public interface IApiAccessTokenProvider
{
    Task<string> GetTokenAsync();
}

public sealed class ApiAccessTokenProvider(
    AuthenticationStateProvider authentication,
    ITokenAcquisition tokens,
    IConfiguration configuration) : IApiAccessTokenProvider
{
    public async Task<string> GetTokenAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();
        var scope = configuration["ApiScope"]
            ?? throw new InvalidOperationException("ApiScope is required.");
        return await tokens.GetAccessTokenForUserAsync([scope], user: state.User);
    }
}
