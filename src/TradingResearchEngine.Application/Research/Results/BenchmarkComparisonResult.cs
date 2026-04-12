using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// V5: Extended benchmark comparison result with excess metrics.
/// Compares a strategy against a buy-and-hold benchmark.
/// </summary>
public sealed record BenchmarkComparisonResult(
    decimal StrategyReturn,
    decimal BenchmarkReturn,
    decimal Alpha,
    decimal? Beta,
    decimal? InformationRatio,
    decimal TrackingError,
    IReadOnlyList<EquityCurvePoint> BenchmarkEquityCurve,
    /// <summary>V5: Excess return = strategy Sharpe - benchmark Sharpe.</summary>
    decimal ExcessReturn = 0m,
    /// <summary>V5: Maximum relative drawdown between strategy and benchmark.</summary>
    decimal? MaxRelativeDrawdown = null);
