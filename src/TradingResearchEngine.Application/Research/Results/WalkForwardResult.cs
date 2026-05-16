using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of a walk-forward analysis workflow.</summary>
public sealed record WalkForwardResult(
    IReadOnlyList<WalkForwardWindow> Windows,
    decimal? MeanEfficiencyRatio,
    /// <summary>Enriched analytics including OOS profitability rate, concatenated equity curve, and parameter drift.</summary>
    WalkForwardAnalytics? Analytics = null)
{
    /// <summary>
    /// Chronologically stitched out-of-sample equity curve combining all OOS window results
    /// in window index order. Provides the standard walk-forward robustness presentation
    /// showing how a parameter-adaptive strategy would perform on unseen data.
    /// </summary>
    public IReadOnlyList<EquityCurvePoint> ConcatenatedOosEquityCurve { get; } =
        Windows
            .OrderBy(w => w.WindowIndex)
            .SelectMany(w => w.OutOfSampleResult.EquityCurve)
            .ToList();
}

/// <summary>A single in-sample / out-of-sample window in a walk-forward analysis.</summary>
public sealed record WalkForwardWindow(
    int WindowIndex,
    BacktestResult InSampleResult,
    BacktestResult OutOfSampleResult,
    Dictionary<string, object> SelectedParameters,
    decimal? EfficiencyRatio,
    decimal? OptimizationMetricValue = null,
    OptimizationObjective UsedObjective = OptimizationObjective.Sharpe);
