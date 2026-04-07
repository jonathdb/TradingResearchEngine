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
        app.MapPost("/scenarios/run", async (ScenarioConfig config, RunScenarioUseCase useCase, CancellationToken ct) =>
        {
            var result = await useCase.RunAsync(config, ct);
            return result.IsSuccess
                ? Results.Ok(result.Result)
                : Results.BadRequest(new { errors = result.Errors!.Select(e => new { field = "", message = e }) });
        }).WithName("RunScenario").WithTags("Scenarios");

        app.MapPost("/scenarios/sweep", async (ScenarioConfig config, ParameterSweepWorkflow workflow, CancellationToken ct) =>
        {
            var options = new SweepOptions();
            var result = await workflow.RunAsync(config, options, ct);
            return Results.Ok(result);
        }).WithName("ParameterSweep").WithTags("Scenarios");

        app.MapPost("/scenarios/montecarlo", async (ScenarioConfig config, MonteCarloWorkflow workflow, CancellationToken ct) =>
        {
            var options = new MonteCarloOptions();
            var mcResult = await workflow.RunAsync(config, options, ct);
            return Results.Ok(mcResult);
        }).WithName("MonteCarlo").WithTags("Scenarios");

        app.MapPost("/scenarios/walkforward", async (ScenarioConfig config, WalkForwardWorkflow workflow, CancellationToken ct) =>
        {
            var options = new WalkForwardOptions();
            var result = await workflow.RunAsync(config, options, ct);
            return Results.Ok(result);
        }).WithName("WalkForward").WithTags("Scenarios");
    }
}
