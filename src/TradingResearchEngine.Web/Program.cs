using MudBlazor.Services;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Infrastructure;
using TradingResearchEngine.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Engine services
builder.Services.AddTradingResearchEngine(builder.Configuration);
builder.Services.AddTradingResearchEngineInfrastructure(builder.Configuration);
builder.Services.AddStrategyAssembly(typeof(SmaCrossoverStrategy).Assembly);

// MudBlazor
builder.Services.AddMudServices();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
