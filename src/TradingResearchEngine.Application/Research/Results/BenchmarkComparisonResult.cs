using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of comparing a strategy against a buy-and-hold benchmark.</summary>
public sealed record BenchmarkComparisonResult(
    decimal StrategyReturn,
    decimal BenchmarkReturn,
    decimal Alpha,
    decimal? Beta,
    decimal? InformationRatio,
    decimal TrackingError,
    IReadOnlyList<EquityCurvePoint> BenchmarkEquityCurve);
