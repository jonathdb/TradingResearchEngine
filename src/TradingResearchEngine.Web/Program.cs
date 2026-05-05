using MudBlazor.Services;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Infrastructure;
using TradingResearchEngine.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Enable static web assets in all environments (required for NuGet package assets like MudBlazor)
builder.WebHost.UseStaticWebAssets();

// Engine services
builder.Services.AddTradingResearchEngine(builder.Configuration);
builder.Services.AddTradingResearchEngineInfrastructure(builder.Configuration);
builder.Services.AddStrategyAssembly(typeof(DonchianBreakoutStrategy).Assembly);

// V7: Staleness configuration
builder.Services.Configure<StalenessOptions>(builder.Configuration.GetSection("Staleness"));

// V7: Robustness advisory service — centralised threshold checks
builder.Services.Configure<TradingResearchEngine.Application.Research.RobustnessThresholds>(
    builder.Configuration.GetSection("RobustnessThresholds"));
builder.Services.AddSingleton<TradingResearchEngine.Application.Research.IRobustnessAdvisoryService,
    TradingResearchEngine.Application.Research.RobustnessAdvisoryService>();

// V5: Register JobExecutor as singleton for async job lifecycle management
builder.Services.AddSingleton<TradingResearchEngine.Application.Research.JobExecutor>();

// MudBlazor
builder.Services.AddMudServices();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// V4: Run migration on startup (non-blocking, failure-safe)
using (var scope = app.Services.CreateScope())
{
    var migration = scope.ServiceProvider.GetRequiredService<TradingResearchEngine.Infrastructure.Persistence.MigrationService>();
    await migration.MigrateIfNeededAsync();
}

// Market Data: recover orphaned imports on startup
try
{
    var importService = app.Services.GetRequiredService<TradingResearchEngine.Application.MarketData.MarketDataImportService>();
    await importService.RecoverOnStartupAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Market data import recovery failed on startup");
}

// V5: Recover orphaned jobs (Queued/Running) from previous process lifetime
try
{
    var jobExecutor = app.Services.GetRequiredService<TradingResearchEngine.Application.Research.JobExecutor>();
    await jobExecutor.RecoverOrphanedJobsAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Job recovery failed on startup");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
