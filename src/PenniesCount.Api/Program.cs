using PenniesCount.Contracts;
using Microsoft.EntityFrameworkCore;
using PenniesCount.Api.Data;
using PenniesCount.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5100");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<SavingsDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Savings")
    ?? throw new InvalidOperationException("ConnectionStrings:Savings is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/api/health", () => new HealthResponse("Ready"));
app.MapAccounts();
app.MapSavingsItems();

app.Run();

public partial class Program;
