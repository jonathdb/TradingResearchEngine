using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Core.PaperTrading;

/// <summary>
/// An immutable event emitted when a position is closed during a paper trading session,
/// containing the closed trade, timestamp, and a snapshot of the portfolio state.
/// </summary>
public sealed record PaperTradeEvent(
    /// <summary>The closed trade that triggered this event.</summary>
    ClosedTrade Trade,
    /// <summary>The timestamp when this event was emitted.</summary>
    DateTimeOffset Timestamp,
    /// <summary>Portfolio state after the trade was closed.</summary>
    PortfolioSnapshot Snapshot);
