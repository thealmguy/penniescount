using PenniesCount.Web.Components;
using PenniesCount.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

var useEntra = builder.Configuration.GetSection("AzureAd").Exists();
if (!useEntra && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("AzureAd configuration is required outside Development.");
if (useEntra)
{
    var allowedUsers = builder.Configuration.GetSection("AllowedUserObjectIds").Get<string[]>()
        ?? throw new InvalidOperationException("AllowedUserObjectIds is required.");
    if (allowedUsers.Length == 0)
        throw new InvalidOperationException("AllowedUserObjectIds must not be empty.");
    var scope = builder.Configuration["ApiScope"]
        ?? throw new InvalidOperationException("ApiScope is required.");
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi([scope])
        .AddInMemoryTokenCaches();
    builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
        options => options.AccessDeniedPath = "/access-denied");
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                allowedUsers.Contains(context.User.GetObjectId(), StringComparer.OrdinalIgnoreCase))
            .Build();
    });
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IApiAccessTokenProvider, ApiAccessTokenProvider>();
    builder.Services.AddMicrosoftIdentityConsentHandler();
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient("BudgetApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped(services => new BudgetClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("BudgetApi"),
    services.GetService<IApiAccessTokenProvider>()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (useEntra)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/access-denied", () => Results.Text("This account is not permitted to use PenniesCount.",
    statusCode: StatusCodes.Status403Forbidden)).AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
