using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Evaluates a <see cref="BacktestResult"/> against configurable robustness thresholds
/// and returns human-readable warning strings for any violations.
/// </summary>
public interface IRobustnessAdvisoryService
{
    /// <summary>
    /// Returns a list of warning strings for metrics that violate robustness thresholds.
    /// An empty list indicates no warnings.
    /// </summary>
    /// <param name="result">The backtest result to evaluate.</param>
    /// <returns>A read-only list of warning descriptions.</returns>
    IReadOnlyList<string> GetWarnings(BacktestResult result);

    /// <summary>
    /// Returns structured warnings with severity, explanation, cause, and recommended action.
    /// Provides richer context than <see cref="GetWarnings"/> for the Robustness Hub UI.
    /// </summary>
    /// <param name="result">The backtest result to evaluate.</param>
    /// <returns>A read-only list of structured robustness warnings.</returns>
    IReadOnlyList<RobustnessWarning> GetStructuredWarnings(BacktestResult result);
}
