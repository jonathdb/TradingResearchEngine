using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Runs a single backtest against the sealed held-out test set.
/// This is a one-time action that marks the strategy as <see cref="DevelopmentStage.FinalTest"/>.
/// Requires explicit user confirmation before consuming the test set (irreversible).
/// Integrates research checklist state to surface gating warnings when critical items are incomplete.
/// </summary>
public sealed class FinalValidationUseCase
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IStrategyRepository _strategyRepo;
    private readonly SealedTestSetGuard _guard;
    private readonly ITestSetGuard _testSetGuard;
    private readonly ResearchChecklistService _checklistService;
    private readonly ILogger<FinalValidationUseCase> _logger;

    /// <summary>
    /// The explanation displayed to the user before requesting confirmation.
    /// Describes the irreversible consequences of consuming the test set.
    /// </summary>
    public static readonly string ConsequenceExplanation =
        "Final validation will consume the sealed held-out test set for this strategy version. " +
        "This action is irreversible: once consumed, the test set cannot be reused for this version. " +
        "The strategy will be marked as FinalTest and no further validation runs against this data are possible. " +
        "Ensure all research steps are complete before proceeding.";

    /// <summary>
    /// The label to display on the final validation action after the test set has been consumed.
    /// </summary>
    public static readonly string ConsumedActionLabel = "Final Validation (Completed)";

    /// <inheritdoc cref="FinalValidationUseCase"/>
    public FinalValidationUseCase(
        RunScenarioUseCase runScenario,
        IStrategyRepository strategyRepo,
        SealedTestSetGuard guard,
        ITestSetGuard testSetGuard,
        ResearchChecklistService checklistService,
        ILogger<FinalValidationUseCase> logger)
    {
        _runScenario = runScenario;
        _strategyRepo = strategyRepo;
        _guard = guard;
        _testSetGuard = testSetGuard;
        _checklistService = checklistService;
        _logger = logger;
    }

    /// <summary>
    /// Executes final validation with an explicit confirmation gate.
    /// Returns <see cref="FinalValidationResult.Cancelled"/> when the user declines,
    /// <see cref="FinalValidationResult.AlreadyConsumed"/> when the test set was already used,
    /// or <see cref="FinalValidationResult.Success"/> with the backtest result on completion.
    /// Surfaces checklist gating warnings when critical research items are incomplete.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version to validate.</param>
    /// <param name="userConfirmed">Whether the user has explicitly confirmed the irreversible action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="FinalValidationResult"/> describing the outcome.</returns>
    public async Task<FinalValidationResult> ExecuteAsync(
        string strategyVersionId,
        bool userConfirmed,
        CancellationToken ct = default)
    {
        if (!userConfirmed)
            return FinalValidationResult.Cancelled("User declined confirmation.");

        var isConsumed = await _testSetGuard.IsConsumedAsync(strategyVersionId, ct);
        if (isConsumed)
            return FinalValidationResult.AlreadyConsumed(
                "Test set already consumed for this strategy version.");

        // Delegate to the existing RunAsync for the actual validation logic
        var runResult = await RunAsync(strategyVersionId, ct);

        if (!runResult.IsSuccess)
        {
            var errorMessage = runResult.Errors is { Count: > 0 }
                ? string.Join("; ", runResult.Errors)
                : "Final validation failed.";
            return FinalValidationResult.Failed(errorMessage);
        }

        // Mark the test set as consumed after successful validation
        await _testSetGuard.MarkConsumedAsync(strategyVersionId, ct);

        _logger.LogInformation(
            "Final validation completed and test set consumed for strategy version '{StrategyVersionId}'.",
            strategyVersionId);

        return FinalValidationResult.Success(runResult.Result!);
    }

    /// <summary>
    /// Evaluates checklist readiness for the final validation flow.
    /// Returns warnings when critical research items are incomplete, allowing the UI
    /// to display checklist status before the user confirms the irreversible action.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readiness result with warnings and incomplete item details.</returns>
    public async Task<ChecklistReadinessResult> GetChecklistReadinessAsync(
        string strategyVersionId,
        CancellationToken ct = default)
    {
        return await _checklistService.EvaluateReadinessAsync(strategyVersionId, ct);
    }

    /// <summary>
    /// Returns whether the final validation action is available (test set not yet consumed)
    /// for the specified strategy version.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when final validation can still be performed; <c>false</c> when the test set is consumed.</returns>
    public async Task<bool> IsAvailableAsync(string strategyVersionId, CancellationToken ct = default)
    {
        var isConsumed = await _testSetGuard.IsConsumedAsync(strategyVersionId, ct);
        return !isConsumed;
    }

    /// <summary>
    /// Returns the appropriate action label for the final validation button/action.
    /// When the test set is consumed, returns <see cref="ConsumedActionLabel"/>;
    /// otherwise returns "Run Final Validation".
    /// </summary>
    /// <param name="strategyVersionId">The strategy version identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The label text for the final validation action.</returns>
    public async Task<string> GetActionLabelAsync(string strategyVersionId, CancellationToken ct = default)
    {
        var isConsumed = await _testSetGuard.IsConsumedAsync(strategyVersionId, ct);
        return isConsumed ? ConsumedActionLabel : "Run Final Validation";
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

        // On success, mark the strategy as FinalTest and record the unlock
        if (result.IsSuccess && result.Result?.Status == BacktestStatus.Completed)
        {
            var strategy = await _strategyRepo.GetAsync(version.StrategyId, ct);
            if (strategy is not null)
            {
                var updated = strategy with { Stage = DevelopmentStage.FinalTest };
                await _strategyRepo.SaveAsync(updated, ct);

                // Record the unlock in the audit log
                if (Guid.TryParse(version.StrategyVersionId, out var versionGuid))
                {
                    await _guard.RecordUnlockAsync(versionGuid, "Final validation run completed successfully.", ct);
                }

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
