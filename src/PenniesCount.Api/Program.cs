using PenniesCount.Contracts;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Api;
using PenniesCount.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5100");
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IContributionSchedule, FirstOfMonthSchedule>();
builder.Services.AddSingleton<BudgetCalculator>();
builder.Services.AddDbContext<SavingsDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Savings")
    ?? throw new InvalidOperationException("ConnectionStrings:Savings is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapGet("/api/health", () => new HealthResponse("Ready"));
app.MapAccounts();
app.MapSavingsItems();
app.MapDashboard();

app.Run();

public partial class Program;
