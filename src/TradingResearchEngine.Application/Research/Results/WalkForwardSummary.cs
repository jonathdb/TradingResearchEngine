using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// Enhanced walk-forward result with composite out-of-sample equity curve
/// and parameter drift metrics.
/// </summary>
public sealed record WalkForwardSummary(
    IReadOnlyList<WalkForwardWindow> Windows,
    IReadOnlyList<EquityCurvePoint> CompositeEquityCurve,
    decimal? AverageOutOfSampleSharpe,
    decimal WorstWindowDrawdown,
    decimal ParameterDriftScore,
    decimal? MeanEfficiencyRatio);
