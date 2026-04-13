using TradingResearchEngine.Api.Dtos;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Api.Endpoints;

/// <summary>Maps discovery endpoints for strategies, schemas, workflows, presets, and execution models.</summary>
public static class DiscoveryEndpoints
{
    /// <summary>Registers all discovery endpoints on the route builder.</summary>
    public static void MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/strategies", ListStrategies)
            .WithName("ListStrategies").WithTags("Discovery")
            .Produces<IReadOnlyList<StrategyListItem>>();

        app.MapGet("/strategies/{name}/schema", GetStrategySchema)
            .WithName("GetStrategySchema").WithTags("Discovery")
            .Produces<SchemaResponse>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/workflows", ListWorkflows)
            .WithName("ListWorkflows").WithTags("Discovery")
            .Produces<IReadOnlyList<object>>();

        app.MapGet("/presets", ListPresets)
            .WithName("ListPresets").WithTags("Discovery")
            .Produces<IReadOnlyList<ConfigPreset>>();

        app.MapGet("/execution-models", ListExecutionModels)
            .WithName("ListExecutionModels").WithTags("Discovery")
            .Produces<ExecutionModelsResponse>();
    }

    private static IResult ListStrategies(
        IReadOnlyList<StrategyTemplate> templates)
    {
        var items = templates
            .Where(t => t.Descriptor is not null)
            .Select(t => new StrategyListItem(
                t.StrategyType,
                t.Descriptor!.DisplayName,
                t.Descriptor.Family,
                t.Descriptor.Description,
                t.Descriptor.Hypothesis,
                t.Descriptor.BestFor,
                t.Descriptor.SuggestedStudies,
                t.DifficultyLevel))
            .ToList();

        return Results.Ok(items);
    }

    private static IResult GetStrategySchema(
        string name,
        IStrategySchemaProvider schemaProvider)
    {
        try
        {
            var parameters = schemaProvider.GetSchema(name);
            var response = new SchemaResponse(
                name,
                SchemaVersion: "1.0",
                Parameters: parameters,
                DeprecatedFields: null,
                CompatibilityNotes: null);
            return Results.Ok(response);
        }
        catch (StrategyNotFoundException)
        {
            return Results.NotFound(new { error = $"Strategy '{name}' not found." });
        }
    }

    private static IResult ListWorkflows()
    {
        var workflows = new[]
        {
            new { Name = "SingleRun", Description = "Execute a single backtest run.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Quick hypothesis validation." },
            new { Name = "MonteCarlo", Description = "Monte Carlo simulation with resampled returns.", RequiredParameters = new[] { "SimulationCount" }, TypicalUseCase = "Assess strategy robustness under randomized conditions." },
            new { Name = "WalkForward", Description = "Walk-forward analysis with rolling IS/OOS windows.", RequiredParameters = new[] { "WindowCount" }, TypicalUseCase = "Detect overfitting via out-of-sample validation." },
            new { Name = "ParameterSweep", Description = "Grid search over parameter combinations.", RequiredParameters = new[] { "ParameterRanges" }, TypicalUseCase = "Find optimal parameter regions." },
            new { Name = "Sensitivity", Description = "Sensitivity analysis varying cost and delay assumptions.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Measure strategy fragility to execution assumptions." },
            new { Name = "Stability", Description = "Parameter stability analysis with fragility scoring.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Identify parameter islands and overfitting risk." },
            new { Name = "Realism", Description = "Realism sensitivity across multiple execution profiles.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Compare strategy performance under different realism levels." },
            new { Name = "Perturbation", Description = "Random parameter perturbation study.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Test parameter robustness via random noise injection." },
            new { Name = "RegimeSegmentation", Description = "Performance breakdown by detected market regime.", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Understand strategy behavior across market conditions." },
            new { Name = "BenchmarkComparison", Description = "Compare strategy against a benchmark (default: buy-and-hold).", RequiredParameters = Array.Empty<string>(), TypicalUseCase = "Quantify strategy edge over passive exposure." },
        };

        return Results.Ok(workflows);
    }

    private static async Task<IResult> ListPresets(
        IRepository<ConfigPreset> presetRepo,
        CancellationToken ct)
    {
        var custom = await presetRepo.ListAsync(ct);
        var all = DefaultConfigPresets.All.Concat(custom).ToList();
        return Results.Ok(all);
    }

    private static IResult ListExecutionModels()
    {
        var response = new ExecutionModelsResponse(
            SlippageModels: new NamedItem[]
            {
                new("ZeroSlippageModel", "No slippage applied."),
                new("FixedSpreadSlippageModel", "Fixed spread-based slippage."),
                new("PercentOfPriceSlippageModel", "Slippage as a percentage of price."),
                new("AtrScaledSlippageModel", "ATR-scaled slippage for volatility-aware fills."),
            },
            CommissionModels: new NamedItem[]
            {
                new("ZeroCommissionModel", "No commission applied."),
                new("PerTradeCommissionModel", "Fixed commission per trade."),
                new("PerShareCommissionModel", "Commission per share traded."),
            },
            FillModes: new NamedItem[]
            {
                new("NextBarOpen", "Orders fill at the next bar's open price."),
                new("IntraBar", "Orders fill within the current bar using limit/stop logic."),
            },
            RealismProfiles: new NamedItem[]
            {
                new("FastResearch", "Zero-cost, relaxed assumptions for quick hypothesis testing."),
                new("StandardBacktest", "Moderate costs and standard assumptions."),
                new("BrokerConservative", "Conservative realism with ATR-scaled slippage and session rules."),
            },
            SessionCalendars: new NamedItem[]
            {
                new("AlwaysOpen", "No session restrictions — all bars are tradeable."),
                new("UsEquitySession", "US equity market hours (9:30–16:00 ET)."),
                new("ForexSession", "24/5 forex sessions (Sydney, Tokyo, London, New York)."),
            },
            PositionSizingPolicies: new NamedItem[]
            {
                new("PercentEquity", "Size positions as a percentage of current equity."),
                new("FixedQuantity", "Fixed number of shares/contracts per trade."),
                new("FixedDollar", "Fixed dollar amount per trade."),
                new("VolatilityTarget", "Target a specific portfolio volatility level."),
            });

        return Results.Ok(response);
    }
}
