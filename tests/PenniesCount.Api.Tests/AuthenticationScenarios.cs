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
    [InlineData("allowed-user", "access_as_user", HttpStatusCode.BadRequest)]
    [InlineData("allowed-user", "other access_as_user", HttpStatusCode.BadRequest)]
    [InlineData("other-user", "access_as_user", HttpStatusCode.Forbidden)]
    [InlineData("allowed-user", "other", HttpStatusCode.Forbidden)]
    [InlineData("allowed-user", "access_as_user_extra", HttpStatusCode.Forbidden)]
    [InlineData("allowed-user", "", HttpStatusCode.Forbidden)]
    public async Task Given_a_valid_identity_When_calling_data_Then_both_user_and_delegated_scope_are_required(
        string objectId, string scope, HttpStatusCode expected)
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
                services.AddSingleton(new IdentityClaims(objectId, scope));
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

    private static WebApplicationFactory<Program> CreateCloudFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
            builder.UseSetting("AzureAd:TenantId", Guid.Empty.ToString());
            builder.UseSetting("AzureAd:ClientId", Guid.Empty.ToString());
            builder.UseSetting("AllowedUserObjectIds:0", "allowed-user");
        });

    private sealed record IdentityClaims(string ObjectId, string Scope);

    private sealed class TestIdentity(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IdentityClaims claims) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new("oid", claims.ObjectId), new("scp", claims.Scope)], Scheme.Name)), Scheme.Name)));
    }
}
