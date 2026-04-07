using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of a parameter sweep workflow.</summary>
public sealed record SweepResult(
    IReadOnlyList<BacktestResult> Results,
    IReadOnlyList<BacktestResult> RankedBySharpe,
    IReadOnlyDictionary<string, decimal> ParameterSensitivity);
