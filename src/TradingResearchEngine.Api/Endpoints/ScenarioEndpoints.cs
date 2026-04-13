using System.Text.Json;
using TradingResearchEngine.Api.Dtos;
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
            PreflightValidator preflightValidator,
            ParameterSweepWorkflow workflow,
            CancellationToken ct) =>
        {
            var (request, isBare) = await DeserializeOrWrap<SweepRequest, ScenarioConfig>(
                httpContext, (c) => new SweepRequest(c));

            AddDeprecationHeaderIfFlat(httpContext, request.Config);
            if (isBare)
                httpContext.Response.Headers["X-Deprecation"] =
                    "Bare ScenarioConfig body is deprecated. Wrap in { \"Config\": ... }.";

            var preflight = preflightValidator.Validate(request.Config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var options = request.Options ?? new SweepOptions();
            var result = await workflow.RunAsync(request.Config, options, ct);
            return Results.Ok(result);
        }).WithName("ParameterSweep").WithTags("Scenarios")
          .Accepts<SweepRequest>("application/json");

        app.MapPost("/scenarios/montecarlo", async (
            HttpContext httpContext,
            PreflightValidator preflightValidator,
            MonteCarloWorkflow workflow,
            CancellationToken ct) =>
        {
            var (request, isBare) = await DeserializeOrWrap<MonteCarloRequest, ScenarioConfig>(
                httpContext, (c) => new MonteCarloRequest(c));

            AddDeprecationHeaderIfFlat(httpContext, request.Config);
            if (isBare)
                httpContext.Response.Headers["X-Deprecation"] =
                    "Bare ScenarioConfig body is deprecated. Wrap in { \"Config\": ... }.";

            var preflight = preflightValidator.Validate(request.Config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var options = request.Options ?? new MonteCarloOptions();
            var mcResult = await workflow.RunAsync(request.Config, options, ct);
            return Results.Ok(mcResult);
        }).WithName("MonteCarlo").WithTags("Scenarios")
          .Accepts<MonteCarloRequest>("application/json");

        app.MapPost("/scenarios/walkforward", async (
            HttpContext httpContext,
            PreflightValidator preflightValidator,
            WalkForwardWorkflow workflow,
            CancellationToken ct) =>
        {
            var (request, isBare) = await DeserializeOrWrap<WalkForwardRequest, ScenarioConfig>(
                httpContext, (c) => new WalkForwardRequest(c));

            AddDeprecationHeaderIfFlat(httpContext, request.Config);
            if (isBare)
                httpContext.Response.Headers["X-Deprecation"] =
                    "Bare ScenarioConfig body is deprecated. Wrap in { \"Config\": ... }.";

            var options = request.Options ?? new WalkForwardOptions();

            // Validate zero-TimeSpan fields on WalkForwardOptions
            var wfErrors = new List<object>();
            if (request.Options is not null)
            {
                if (request.Options.InSampleLength == TimeSpan.Zero)
                    wfErrors.Add(new { field = "Options.InSampleLength", message = "InSampleLength must be non-zero." });
                if (request.Options.OutOfSampleLength == TimeSpan.Zero)
                    wfErrors.Add(new { field = "Options.OutOfSampleLength", message = "OutOfSampleLength must be non-zero." });
                if (request.Options.StepSize == TimeSpan.Zero)
                    wfErrors.Add(new { field = "Options.StepSize", message = "StepSize must be non-zero." });
            }
            if (wfErrors.Count > 0)
                return Results.BadRequest(new { errors = wfErrors });

            var preflight = preflightValidator.Validate(request.Config);
            if (preflight.HasErrors)
                return Results.BadRequest(new
                {
                    errors = preflight.Findings
                        .Where(f => f.Severity == PreflightSeverity.Error)
                        .Select(f => new { field = f.Field, message = f.Message, severity = f.Severity.ToString(), code = f.Code })
                });

            var result = await workflow.RunAsync(request.Config, options, ct);
            return Results.Ok(result);
        }).WithName("WalkForward").WithTags("Scenarios")
          .Accepts<WalkForwardRequest>("application/json");

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

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Two-pass deserialization: checks for a "Config" property to decide between
    /// the typed wrapper and a bare ScenarioConfig body (backward compatibility).
    /// </summary>
    private static async Task<(TWrapper Request, bool IsBare)> DeserializeOrWrap<TWrapper, TBare>(
        HttpContext httpContext, Func<TBare, TWrapper> wrapBare)
    {
        httpContext.Request.EnableBuffering();
        using var doc = await JsonDocument.ParseAsync(httpContext.Request.Body);

        if (doc.RootElement.TryGetProperty("Config", out _) ||
            doc.RootElement.TryGetProperty("config", out _))
        {
            var wrapper = doc.RootElement.Deserialize<TWrapper>(s_jsonOptions)!;
            return (wrapper, false);
        }

        var bare = doc.RootElement.Deserialize<TBare>(s_jsonOptions)!;
        return (wrapBare(bare), true);
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
