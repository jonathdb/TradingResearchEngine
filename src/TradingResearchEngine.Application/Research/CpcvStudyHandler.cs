using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Placeholder for Combinatorial Purged Cross-Validation (CPCV) study.
/// CPCV generates all combinations of training/test splits across N folds
/// and computes the Probability of Backtest Overfitting (PBO).
/// Full implementation is scheduled for V4.1.
/// </summary>
public sealed class CpcvStudyHandler
{
    /// <summary>
    /// Throws <see cref="NotImplementedException"/> — CPCV is deferred to V4.1.
    /// </summary>
    public Task RunAsync(ScenarioConfig config, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "Combinatorial Purged Cross-Validation (CPCV) is scheduled for V4.1. " +
            "The StudyType.CombinatorialPurgedCV enum entry and PBO metric tile are scaffolded " +
            "but the full implementation requires N-fold combinatorial backtest execution.");
    }
}
