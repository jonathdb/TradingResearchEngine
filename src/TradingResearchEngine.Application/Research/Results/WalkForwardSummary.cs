using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// Enhanced walk-forward result with composite out-of-sample equity curve,
/// OOS profitability rate, and parameter drift metrics.
/// </summary>
public sealed record WalkForwardSummary(
    IReadOnlyList<WalkForwardWindow> Windows,
    IReadOnlyList<EquityCurvePoint> CompositeEquityCurve,
    decimal? AverageOutOfSampleSharpe,
    decimal WorstWindowDrawdown,
    decimal ParameterDriftScore,
    decimal? MeanEfficiencyRatio,
    /// <summary>
    /// Fraction of OOS windows that are profitable (OOS EndEquity > StartEquity).
    /// High IS Sharpe combined with low OOS profitability rate is a strong overfitting signal.
    /// </summary>
    decimal OosProfitabilityRate);
