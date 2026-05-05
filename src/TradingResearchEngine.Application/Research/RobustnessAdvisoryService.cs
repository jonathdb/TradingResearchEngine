using Microsoft.Extensions.Options;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Evaluates <see cref="BacktestResult"/> metrics against configurable thresholds
/// and returns warning strings for any violations. Thresholds are loaded via
/// <see cref="IOptions{RobustnessThresholds}"/> so they can be changed in configuration
/// without code modification.
/// </summary>
public sealed class RobustnessAdvisoryService : IRobustnessAdvisoryService
{
    private readonly RobustnessThresholds _thresholds;

    /// <summary>
    /// Initialises a new instance of <see cref="RobustnessAdvisoryService"/>.
    /// </summary>
    /// <param name="options">The configured robustness thresholds.</param>
    public RobustnessAdvisoryService(IOptions<RobustnessThresholds> options)
        => _thresholds = options.Value;

    /// <inheritdoc/>
    public IReadOnlyList<string> GetWarnings(BacktestResult result)
    {
        var warnings = new List<string>();

        if (result.SharpeRatio > _thresholds.MaxSharpeRatio)
            warnings.Add($"Sharpe > {_thresholds.MaxSharpeRatio}");

        if (result.TotalTrades < _thresholds.MinTotalTrades)
            warnings.Add($"{result.TotalTrades} trades");

        if (result.EquityCurveSmoothness < _thresholds.MinKRatio)
            warnings.Add("K-Ratio < 0");

        if (result.MaxDrawdown > _thresholds.MaxDrawdownPercent)
            warnings.Add($"DD {result.MaxDrawdown:P0}");

        return warnings;
    }
}
