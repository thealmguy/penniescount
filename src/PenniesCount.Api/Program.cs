using PenniesCount.Contracts;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;
using PenniesCount.Api;
using PenniesCount.Core;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                context.User.FindAll("http://schemas.microsoft.com/identity/claims/scope")
                    .Concat(context.User.FindAll("scp"))
                    .Any(claim => claim.Value.Split(' ').Contains("access_as_user", StringComparer.Ordinal))
                && allowedUsers.Contains(context.User.GetObjectId(), StringComparer.OrdinalIgnoreCase))
            .Build();
    });
}
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<GovUkBankHolidayProvider>(client =>
{
    client.BaseAddress = new Uri("https://www.gov.uk/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<IBankHolidayProvider>(services =>
    services.GetRequiredService<GovUkBankHolidayProvider>());
builder.Services.AddDbContext<SavingsDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Savings")
    ?? throw new InvalidOperationException("ConnectionStrings:Savings is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (useEntra)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.MapGet("/api/health", () => new HealthResponse("Ready")).AllowAnonymous();
app.MapAccounts();
app.MapSavingsItems();
app.MapSavingsDrawdowns();
app.MapSavingsLedger();
app.MapDashboard();
app.MapFutureLook();

app.Run();

public partial class Program;
