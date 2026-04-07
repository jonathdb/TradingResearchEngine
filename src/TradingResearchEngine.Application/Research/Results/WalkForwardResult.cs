using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of a walk-forward analysis workflow.</summary>
public sealed record WalkForwardResult(
    IReadOnlyList<WalkForwardWindow> Windows,
    decimal? MeanEfficiencyRatio);

/// <summary>A single in-sample / out-of-sample window in a walk-forward analysis.</summary>
public sealed record WalkForwardWindow(
    int WindowIndex,
    BacktestResult InSampleResult,
    BacktestResult OutOfSampleResult,
    Dictionary<string, object> SelectedParameters,
    decimal? EfficiencyRatio);
