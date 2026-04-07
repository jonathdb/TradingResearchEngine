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
    long RunDurationMs) : IHasId
{
    /// <inheritdoc/>
    public string Id => RunId.ToString();
}
