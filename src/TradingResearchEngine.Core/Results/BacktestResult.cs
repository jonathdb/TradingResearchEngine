using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Core.Results;

/// <summary>
/// The structured output of a completed backtest run.
/// Implements <see cref="IHasId"/> so it can be persisted via <c>IRepository&lt;BacktestResult&gt;</c>.
/// </summary>
public sealed record BacktestResult(
    Guid RunId,
    ScenarioConfig ScenarioConfig,
    BacktestStatus Status,
    IReadOnlyList<EquityCurvePoint> EquityCurve,
    IReadOnlyList<ClosedTrade> Trades,
    decimal StartEquity,
    decimal EndEquity,
    decimal MaxDrawdown,
    decimal? SharpeRatio,
    decimal? SortinoRatio,
    decimal? CalmarRatio,
    // decimal? VaR95,
    // decimal? CVaR95,
    // decimal? OmegatRatio,
    // decimal? UlcerIndex,
    decimal? ReturnOnMaxDrawdown,
    int TotalTrades,
    decimal? WinRate,
    decimal? ProfitFactor,
    decimal? AverageWin,
    decimal? AverageLoss,
    decimal? Expectancy,
    TimeSpan? AverageHoldingPeriod,
    decimal? EquityCurveSmoothness,
    int MaxConsecutiveLosses,
    int MaxConsecutiveWins,
    long RunDurationMs,
    decimal? RecoveryFactor = null,
    ExperimentMetadata? Metadata = null,
    IReadOnlyList<EventTraceRecord>? EventTrace = null,
    string? StrategyVersionId = null,
    /// <summary>V4: Exception message and context when <see cref="Status"/> is <see cref="BacktestStatus.Failed"/>.</summary>
    string? FailureDetail = null,
    /// <summary>V4: Deflated Sharpe Ratio adjusted for multiple testing bias (Bailey &amp; López de Prado 2014).</summary>
    decimal? DeflatedSharpeRatio = null,
    /// <summary>V4: Snapshot of <c>StrategyVersion.TotalTrialsRun</c> at the time this run completed.</summary>
    int? TrialCount = null,
    /// <summary>V5: Realism warnings collected during the run (gap fills, volume warnings, session boundary fills).</summary>
    IReadOnlyList<string>? RealismAdvisories = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => RunId.ToString();
}
