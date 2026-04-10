using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Runs a single backtest against the sealed held-out test set.
/// This is a one-time action that marks the strategy as <see cref="DevelopmentStage.FinalTest"/>.
/// </summary>
public sealed class FinalValidationUseCase
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IStrategyRepository _strategyRepo;
    private readonly ILogger<FinalValidationUseCase> _logger;

    /// <inheritdoc cref="FinalValidationUseCase"/>
    public FinalValidationUseCase(
        RunScenarioUseCase runScenario,
        IStrategyRepository strategyRepo,
        ILogger<FinalValidationUseCase> logger)
    {
        _runScenario = runScenario;
        _strategyRepo = strategyRepo;
        _logger = logger;
    }

    /// <summary>
    /// Runs the final validation against the sealed test set.
    /// </summary>
    /// <param name="strategyVersionId">The version to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result, or validation errors if the sealed set is not configured.</returns>
    public async Task<ScenarioRunResult> RunAsync(
        string strategyVersionId,
        CancellationToken ct = default)
    {
        // Find the version
        var version = await FindVersionAsync(strategyVersionId, ct);
        if (version is null)
            return ScenarioRunResult.Failure(new[] { $"Strategy version '{strategyVersionId}' not found." });

        // Validate sealed test set exists
        if (version.SealedTestSet is null)
            return ScenarioRunResult.Failure(new[] { "No sealed test set configured on this version." });

        var sealed_ = version.SealedTestSet.Value;
        if (!sealed_.IsSealed)
            return ScenarioRunResult.Failure(new[] { "The configured date range is not marked as sealed." });

        // Build config scoped to the sealed date range
        var config = version.BaseScenarioConfig with
        {
            DataProviderOptions = new Dictionary<string, object>(version.BaseScenarioConfig.DataProviderOptions)
            {
                ["From"] = sealed_.Start,
                ["To"] = sealed_.End
            }
        };

        // Run the backtest (bypasses sealed-set guard — this IS the final validation)
        var result = await _runScenario.RunAsync(config, ct, autoSave: true);

        // On success, mark the strategy as FinalTest
        if (result.IsSuccess && result.Result?.Status == BacktestStatus.Completed)
        {
            var strategy = await _strategyRepo.GetAsync(version.StrategyId, ct);
            if (strategy is not null)
            {
                var updated = strategy with { Stage = DevelopmentStage.FinalTest };
                await _strategyRepo.SaveAsync(updated, ct);
                _logger.LogInformation(
                    "Strategy '{StrategyId}' marked as FinalTest after final validation run.",
                    strategy.StrategyId);
            }
        }

        return result;
    }

    private async Task<StrategyVersion?> FindVersionAsync(string versionId, CancellationToken ct)
    {
        var strategies = await _strategyRepo.ListAsync(ct);
        foreach (var s in strategies)
        {
            var versions = await _strategyRepo.GetVersionsAsync(s.StrategyId, ct);
            var match = versions.FirstOrDefault(v => v.StrategyVersionId == versionId);
            if (match is not null) return match;
        }
        return null;
    }
}
