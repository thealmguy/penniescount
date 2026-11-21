using PenniesCount.Contracts;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Api.Holidays;
using PenniesCount.Api;
using PenniesCount.Core;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5100");
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
app.MapGet("/api/health", () => new HealthResponse("Ready"));
app.MapAccounts();
app.MapSavingsItems();
app.MapSavingsDrawdowns();
app.MapSavingsLedger();
app.MapDashboard();

app.Run();

public partial class Program;
