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

    /// <inheritdoc/>
    public IReadOnlyList<RobustnessWarning> GetStructuredWarnings(BacktestResult result)
    {
        return GetStructuredWarnings(result, parameterDriftScore: null);
    }

    /// <inheritdoc/>
    public IReadOnlyList<RobustnessWarning> GetStructuredWarnings(BacktestResult result, decimal? parameterDriftScore)
    {
        var warnings = new List<RobustnessWarning>();

        if (result.SharpeRatio > _thresholds.MaxSharpeRatio)
        {
            warnings.Add(new RobustnessWarning(
                RobustnessSeverity.High,
                "HIGH_SHARPE",
                $"Sharpe ratio ({result.SharpeRatio:F2}) exceeds {_thresholds.MaxSharpeRatio}",
                "Run a walk-forward study to validate out-of-sample performance",
                Cause: "Sharpe > 3.0 often indicates curve-fitting to noise in the training period",
                Remediation: "Run walk-forward or CPCV study to confirm OOS performance",
                CauseCategory: "Overfitting"));
        }

        if (result.TotalTrades < _thresholds.MinTotalTrades)
        {
            warnings.Add(new RobustnessWarning(
                RobustnessSeverity.Medium,
                "LOW_TRADES",
                $"Only {result.TotalTrades} trades (minimum {_thresholds.MinTotalTrades} recommended)",
                "Extend the backtest period or reduce entry signal selectivity",
                Cause: "Low trade count produces unreliable statistics — confidence intervals are wide",
                Remediation: "Use a longer data period or relax entry conditions to generate more trades",
                CauseCategory: "InsufficientData"));
        }

        if (result.EquityCurveSmoothness < _thresholds.MinKRatio)
        {
            warnings.Add(new RobustnessWarning(
                RobustnessSeverity.Medium,
                "NEGATIVE_KRATIO",
                "K-Ratio is negative — equity curve is deteriorating over time",
                "Review strategy logic for regime dependency or parameter decay",
                Cause: "A negative K-Ratio means later performance is worse than earlier, suggesting the edge is decaying",
                Remediation: "Run regime segmentation to identify when the strategy stops working",
                CauseCategory: "ParameterFragility"));
        }

        if (result.MaxDrawdown > _thresholds.MaxDrawdownPercent)
        {
            warnings.Add(new RobustnessWarning(
                RobustnessSeverity.High,
                "EXCESSIVE_DRAWDOWN",
                $"Maximum drawdown ({result.MaxDrawdown:P1}) exceeds {_thresholds.MaxDrawdownPercent:P0} threshold",
                "Review position sizing and risk management parameters",
                Cause: "Excessive drawdown indicates inadequate risk controls or concentrated exposure",
                Remediation: "Reduce position size, add stop-loss, or diversify across assets",
                CauseCategory: "ExecutionUnrealism"));
        }

        if (parameterDriftScore is not null && parameterDriftScore > _thresholds.ParameterDriftThreshold)
        {
            warnings.Add(new RobustnessWarning(
                RobustnessSeverity.High,
                "HIGH_PARAMETER_DRIFT",
                $"Parameter drift score ({parameterDriftScore:F2}) exceeds threshold ({_thresholds.ParameterDriftThreshold:F2})",
                "Strategy is highly sensitive to parameter choice — walk-forward gains may not be reproducible",
                Cause: "High drift indicates optimal parameters change significantly between windows",
                Remediation: "Reduce parameter sensitivity or use wider parameter ranges",
                CauseCategory: "ParameterFragility"));
        }

        return warnings;
    }
}
