using MudBlazor.Services;
using TradingResearchEngine.Application;
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
