using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PenniesCount.Web.Tests;

public sealed class AccessRoleScenarios
{
    [Theory]
    [InlineData("roles", "PenniesCount.User", HttpStatusCode.NotFound)]
    [InlineData(ClaimTypes.Role, "PenniesCount.User", HttpStatusCode.NotFound)]
    [InlineData("roles", "PenniesCount.Admin", HttpStatusCode.Forbidden)]
    [InlineData("roles", "penniescount.user", HttpStatusCode.Forbidden)]
    [InlineData("other", "PenniesCount.User", HttpStatusCode.Forbidden)]
    public async Task Given_a_signed_in_user_When_browsing_Then_the_app_role_is_required(
        string claimType, string role, HttpStatusCode expected)
    {
        using var factory = CreateCloudFactory(new IdentityClaims(claimType, role));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/role-probe");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Given_an_anonymous_user_When_browsing_Then_sign_in_is_challenged()
    {
        using var factory = CreateCloudFactory(null);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/role-probe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateCloudFactory(IdentityClaims? claims) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
            builder.UseSetting("AzureAd:TenantId", Guid.Empty.ToString());
            builder.UseSetting("AzureAd:ClientId", Guid.Empty.ToString());
            builder.UseSetting("ApiScope", "api://test/access_as_user");
            builder.UseSetting("ApiBaseUrl", "https://localhost/");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Test";
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestIdentity>("Test", _ => { });
                services.AddSingleton(new ClaimsHolder(claims));
            });
        });

    private sealed record IdentityClaims(string Type, string Value);

    private sealed record ClaimsHolder(IdentityClaims? Claims);

    private sealed class TestIdentity(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ClaimsHolder holder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            holder.Claims is not { } claims
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [new("oid", "user"), new(claims.Type, claims.Value)], Scheme.Name)), Scheme.Name)));
    }
}
