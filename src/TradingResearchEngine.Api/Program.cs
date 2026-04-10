using TradingResearchEngine.Api.Endpoints;
using TradingResearchEngine.Api.Middleware;
using TradingResearchEngine.Application;
using TradingResearchEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTradingResearchEngine(builder.Configuration);
builder.Services.AddTradingResearchEngineInfrastructure(builder.Configuration);
builder.Services.AddStrategyAssembly(typeof(TradingResearchEngine.Application.Strategies.DonchianBreakoutStrategy).Assembly);
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    // OpenAPI available via Microsoft.AspNetCore.OpenApi
}

app.MapScenarioEndpoints();

app.Run();
