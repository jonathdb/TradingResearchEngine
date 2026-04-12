using System.Text.Json;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Api.Endpoints;

/// <summary>Maps scenario-related API endpoints.</summary>
public static class ScenarioEndpoints
{
    /// <summary>Registers all scenario endpoints on the route builder.</summary>
    public static void MapScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/scenarios/run", async (
            HttpContext httpContext,
            ScenarioConfig config,
            PreflightValidator preflightValidator,
            RunScenarioUseCase useCase,
            CancellationToken ct) =>
        {
            AddDeprecationHeaderIfFlat(httpContext, config);

            var preflight = preflightValidator.Validate(config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var result = await useCase.RunAsync(config, ct);
            return result.IsSuccess
                ? Results.Ok(result.Result)
                : Results.BadRequest(new { errors = result.Errors!.Select(e => new { field = "", message = e }) });
        }).WithName("RunScenario").WithTags("Scenarios")
          .Produces<Core.Results.BacktestResult>()
          .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/scenarios/sweep", async (
            HttpContext httpContext,
            ScenarioConfig config,
            PreflightValidator preflightValidator,
            ParameterSweepWorkflow workflow,
            CancellationToken ct) =>
        {
            AddDeprecationHeaderIfFlat(httpContext, config);

            var preflight = preflightValidator.Validate(config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var options = new SweepOptions();
            var result = await workflow.RunAsync(config, options, ct);
            return Results.Ok(result);
        }).WithName("ParameterSweep").WithTags("Scenarios");

        app.MapPost("/scenarios/montecarlo", async (
            HttpContext httpContext,
            ScenarioConfig config,
            PreflightValidator preflightValidator,
            MonteCarloWorkflow workflow,
            CancellationToken ct) =>
        {
            AddDeprecationHeaderIfFlat(httpContext, config);

            var preflight = preflightValidator.Validate(config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var options = new MonteCarloOptions();
            var mcResult = await workflow.RunAsync(config, options, ct);
            return Results.Ok(mcResult);
        }).WithName("MonteCarlo").WithTags("Scenarios");

        app.MapPost("/scenarios/walkforward", async (
            HttpContext httpContext,
            ScenarioConfig config,
            PreflightValidator preflightValidator,
            WalkForwardWorkflow workflow,
            CancellationToken ct) =>
        {
            AddDeprecationHeaderIfFlat(httpContext, config);

            var preflight = preflightValidator.Validate(config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var options = new WalkForwardOptions();
            var result = await workflow.RunAsync(config, options, ct);
            return Results.Ok(result);
        }).WithName("WalkForward").WithTags("Scenarios");

        app.MapPost("/scenarios/resolve", async (
            ScenarioConfig config,
            ResolvedConfigService resolvedConfigService,
            CancellationToken ct) =>
        {
            var resolved = await resolvedConfigService.ResolveAsync(config, presetId: null, ct);
            return Results.Ok(resolved);
        }).WithName("ResolveConfig").WithTags("Scenarios")
          .Produces<ResolvedConfig>();
    }

    /// <summary>
    /// Adds an X-Deprecation header when the request uses the flat ScenarioConfig format
    /// (no V5 sub-objects present).
    /// </summary>
    private static void AddDeprecationHeaderIfFlat(HttpContext httpContext, ScenarioConfig config)
    {
        if (config.Data is null && config.Strategy is null && config.Risk is null
            && config.Execution is null && config.Research is null)
        {
            httpContext.Response.Headers["X-Deprecation"] =
                "Flat ScenarioConfig format is deprecated; use sub-object format. See /docs/migration.";
        }
    }
}
