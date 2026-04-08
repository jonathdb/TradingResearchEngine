using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of running a strategy under multiple realism profiles.</summary>
public sealed record RealismSensitivityResult(
    IReadOnlyList<RealismProfileResult> ProfileResults,
    decimal SharpeDropFastToStandard,
    decimal SharpeDropStandardToConservative);

/// <summary>Metrics for a single realism profile run.</summary>
public sealed record RealismProfileResult(
    ExecutionRealismProfile Profile,
    BacktestResult Result,
    decimal CAGR,
    decimal? Sharpe,
    decimal MaxDrawdown,
    decimal? ProfitFactor);

/// <summary>Options for realism sensitivity workflow.</summary>
public sealed class RealismSensitivityOptions { }
