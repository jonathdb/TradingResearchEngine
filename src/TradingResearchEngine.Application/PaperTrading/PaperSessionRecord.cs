using TradingResearchEngine.Core.PaperTrading;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.PaperTrading;

/// <summary>
/// Persisted metadata for a paper trading session, tracking lifecycle state
/// and summary metrics for historical review and comparison with backtests.
/// </summary>
public sealed record PaperSessionRecord(
    /// <summary>Unique identifier for this paper trading session.</summary>
    string Id,
    /// <summary>The strategy version used for this session.</summary>
    string StrategyVersionId,
    /// <summary>When the session was started.</summary>
    DateTimeOffset StartedAt,
    /// <summary>When the session was stopped, or null if still active.</summary>
    DateTimeOffset? StoppedAt,
    /// <summary>Current lifecycle status of the session.</summary>
    PaperTradingStatus Status,
    /// <summary>Final profit/loss when the session is stopped, or null if still active.</summary>
    decimal? FinalPnl,
    /// <summary>Total number of closed trades during the session.</summary>
    int TradeCount) : IHasId;
