using TradingResearchEngine.Api.Endpoints;
using TradingResearchEngine.Api.Middleware;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTradingResearchEngine(builder.Configuration);
builder.Services.AddTradingResearchEngineInfrastructure(builder.Configuration);
builder.Services.AddStrategyAssembly(typeof(TradingResearchEngine.Application.Strategies.DonchianBreakoutStrategy).Assembly);
builder.Services.AddEndpointsApiExplorer();

// V5: Register JobExecutor as singleton for async job lifecycle management
builder.Services.AddSingleton<JobExecutor>();

// V5.1: Background job worker with configurable polling
builder.Services.Configure<JobWorkerOptions>(builder.Configuration.GetSection("JobWorker"));
builder.Services.AddHostedService<JobWorkerService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// V5: Recover orphaned jobs (Queued/Running) from previous process lifetime
var jobExecutor = app.Services.GetRequiredService<JobExecutor>();
await jobExecutor.RecoverOrphanedJobsAsync();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    // OpenAPI available via Microsoft.AspNetCore.OpenApi
}

app.MapScenarioEndpoints();
app.MapJobEndpoints();
app.MapDiscoveryEndpoints();

app.Run();
