using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Core.PaperTrading;

/// <summary>
/// The final result produced when a paper trading session stops.
/// Contains the final portfolio state, closed trades, and metrics equivalent
/// to a <see cref="BacktestResult"/> for direct comparison with historical backtests.
/// </summary>
public sealed record PaperTradingResult(
    /// <summary>The portfolio state at the time the session was stopped.</summary>
    Portfolio.Portfolio FinalPortfolio,
    /// <summary>All trades closed during the session.</summary>
    IReadOnlyList<ClosedTrade> ClosedTrades,
    /// <summary>Metrics computed identically to a backtest result for comparison.</summary>
    BacktestResult EquivalentBacktestResult,
    /// <summary>The final status of the session (Stopped or Error).</summary>
    PaperTradingStatus FinalStatus,
    /// <summary>When the session was started.</summary>
    DateTimeOffset StartedAt,
    /// <summary>When the session was stopped.</summary>
    DateTimeOffset StoppedAt);
