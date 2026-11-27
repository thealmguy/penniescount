using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace PenniesCount.Api.Tests;

public sealed class AuthenticationScenarios
{
    [Theory]
    [InlineData("web-client", "access_as_user", HttpStatusCode.BadRequest)]
    [InlineData("WEB-CLIENT", "other access_as_user", HttpStatusCode.BadRequest)]
    [InlineData("other-client", "access_as_user", HttpStatusCode.Forbidden)]
    [InlineData(null, "access_as_user", HttpStatusCode.Forbidden)]
    [InlineData("web-client", "other", HttpStatusCode.Forbidden)]
    [InlineData("web-client", "access_as_user_extra", HttpStatusCode.Forbidden)]
    [InlineData("web-client", "", HttpStatusCode.Forbidden)]
    public async Task Given_a_valid_identity_When_calling_data_Then_the_web_client_and_delegated_scope_are_required(
        string? clientId, string scope, HttpStatusCode expected)
    {
        using var factory = CreateCloudFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestIdentity>("Test", _ => { });
                services.AddSingleton(new IdentityClaims(clientId, scope));
            }));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/accounts", new StringContent(
            "{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Given_cloud_auth_When_anonymous_Then_data_is_protected_but_health_is_available()
    {
        using var factory = CreateCloudFactory();
        using var client = factory.CreateClient();

        using var data = await client.GetAsync("/api/accounts");
        using var health = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, data.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public void Given_no_auth_config_When_hosting_outside_development_Then_startup_fails_closed()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AzureAd configuration is required", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Given_cloud_auth_without_an_authorized_client_When_starting_Then_startup_fails_closed(string? clientId)
    {
        using var factory = CreateCloudFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("AuthorizedClientId", clientId));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AuthorizedClientId is required", error.Message);
    }

    private static WebApplicationFactory<Program> CreateCloudFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
            builder.UseSetting("AzureAd:TenantId", Guid.Empty.ToString());
            builder.UseSetting("AzureAd:ClientId", Guid.Empty.ToString());
            builder.UseSetting("AuthorizedClientId", "web-client");
        });

    private sealed record IdentityClaims(string? ClientId, string Scope);

    private sealed class TestIdentity(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IdentityClaims claims) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            List<Claim> identity = [new("oid", "user"), new("scp", claims.Scope)];
            if (claims.ClientId is not null)
                identity.Add(new("azp", claims.ClientId));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(identity, Scheme.Name)), Scheme.Name)));
        }
    }
}
