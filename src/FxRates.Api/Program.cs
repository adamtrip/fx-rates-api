using FxRates.Api.Endpoints;
using FxRates.Api.ErrorHandling;
using FxRates.Api.RateLimiting;
using FxRates.Infrastructure;
using FxRates.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON for log shippers in containers; plain text when a person is reading a terminal.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole();
}
else
{
    builder.Logging.AddJsonConsole();
}

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// Route binding errors through the exception handler for consistent Problem Details.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<RatesDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

var rateLimiting = app.Services.GetRequiredService<IOptions<RateLimitSettings>>().Value;
app.MapRateEndpoints(rateLimiting.Enabled);
app.MapOpenApi();
app.MapScalarApiReference("/docs", options => options.WithTitle("FX rates API"));
app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();
// Liveness runs no checks: the process answering is the signal. Readiness queries PostgreSQL.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

/// <summary>Exposed so the integration tests can host the application with WebApplicationFactory.</summary>
public partial class Program;
