using TradingResearchEngine.Application.Portfolio;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Api.Endpoints;

/// <summary>Maps portfolio backtest API endpoints.</summary>
public static class PortfolioEndpoints
{
    /// <summary>Registers all portfolio endpoints on the route builder.</summary>
    public static void MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/portfolios/run", async (
            PortfolioBacktestConfig config,
            PortfolioBacktestRunner runner,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateConfig(config);
            if (validationErrors.Count > 0)
            {
                return Results.BadRequest(new { errors = validationErrors });
            }

            var progress = new NullProgressReporter();
            var result = await runner.RunAsync(config, progress, ct);
            return Results.Ok(result);
        }).WithName("RunPortfolio").WithTags("Portfolios")
          .Produces<PortfolioBacktestResult>()
          .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/portfolios/sweep", async (
            PortfolioSweepRequest request,
            PortfolioBacktestRunner runner,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateConfig(request.Config);
            if (validationErrors.Count > 0)
            {
                return Results.BadRequest(new { errors = validationErrors });
            }

            var progress = new NullProgressReporter();
            var results = new List<PortfolioBacktestResult>();

            // Run each parameter variation
            foreach (var variation in request.Variations)
            {
                var variedConfig = ApplyVariation(request.Config, variation);
                var result = await runner.RunAsync(variedConfig, progress, ct);
                results.Add(result);
            }

            return Results.Ok(results);
        }).WithName("SweepPortfolio").WithTags("Portfolios")
          .Produces<List<PortfolioBacktestResult>>()
          .Produces(StatusCodes.Status400BadRequest);
    }

    private static List<object> ValidateConfig(PortfolioBacktestConfig config)
    {
        var errors = new List<object>();

        if (config.Symbols.Count == 0)
            errors.Add(new { field = "Symbols", message = "Portfolio must contain at least one symbol." });

        if (config.Strategies.Count == 0)
            errors.Add(new { field = "Strategies", message = "Portfolio must contain at least one strategy." });

        if (config.Strategies.Count > 1 && config.Strategies.Count != config.Symbols.Count)
            errors.Add(new { field = "Strategies", message = $"Strategy count must be 1 (applied to all symbols) or equal to symbol count ({config.Symbols.Count}). Got {config.Strategies.Count}." });

        if (config.InitialCash <= 0)
            errors.Add(new { field = "InitialCash", message = "InitialCash must be greater than zero." });

        return errors;
    }

    private static PortfolioBacktestConfig ApplyVariation(
        PortfolioBacktestConfig baseConfig,
        PortfolioVariation variation)
    {
        return baseConfig with
        {
            InitialCash = variation.InitialCash ?? baseConfig.InitialCash,
            PortfolioRisk = variation.PortfolioRisk ?? baseConfig.PortfolioRisk
        };
    }

    /// <summary>No-op progress reporter for API endpoints.</summary>
    private sealed class NullProgressReporter : IProgressReporter
    {
        public void Report(int current, int total, string label) { }
        public void Report(ProgressSnapshot snapshot) { }
    }
}

/// <summary>Request body for portfolio sweep endpoint.</summary>
public sealed record PortfolioSweepRequest(
    PortfolioBacktestConfig Config,
    IReadOnlyList<PortfolioVariation> Variations);

/// <summary>A single parameter variation for portfolio sweep.</summary>
public sealed record PortfolioVariation(
    decimal? InitialCash = null,
    PortfolioRiskConfig? PortfolioRisk = null);
