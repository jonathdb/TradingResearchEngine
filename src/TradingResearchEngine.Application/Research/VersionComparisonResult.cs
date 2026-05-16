using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Result of comparing two <see cref="Strategies.StrategyVersion"/> instances side by side.
/// Pins the comparison to specific strategy versions (distinct from arbitrary BacktestResult comparison)
/// and displays metric deltas between the latest runs of each version.
/// </summary>
/// <param name="VersionIdA">The first strategy version ID in the comparison.</param>
/// <param name="VersionIdB">The second strategy version ID in the comparison.</param>
/// <param name="ResultA">The latest <see cref="BacktestResult"/> for version A.</param>
/// <param name="ResultB">The latest <see cref="BacktestResult"/> for version B.</param>
/// <param name="SharpeDelta">Difference in Sharpe ratio (B − A). Null if either version lacks a Sharpe value.</param>
/// <param name="MaxDrawdownDelta">Difference in max drawdown (B − A). Positive means B has higher drawdown.</param>
/// <param name="WinRateDelta">Difference in win rate (B − A). Null if either version lacks a win rate value.</param>
/// <param name="TotalTradesDelta">Difference in total trades (B − A).</param>
/// <param name="EndEquityDelta">Difference in end equity (B − A).</param>
public sealed record VersionComparisonResult(
    string VersionIdA,
    string VersionIdB,
    BacktestResult ResultA,
    BacktestResult ResultB,
    decimal? SharpeDelta,
    decimal MaxDrawdownDelta,
    decimal? WinRateDelta,
    int TotalTradesDelta,
    decimal EndEquityDelta);
