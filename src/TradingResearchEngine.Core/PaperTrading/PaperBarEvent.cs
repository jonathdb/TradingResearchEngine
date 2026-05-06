using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Core.PaperTrading;

/// <summary>
/// An immutable event emitted on each bar during a paper trading session,
/// containing the bar data, timestamp, and a snapshot of the portfolio state.
/// </summary>
public sealed record PaperBarEvent(
    /// <summary>The bar that was processed.</summary>
    BarRecord Bar,
    /// <summary>The timestamp when this event was emitted.</summary>
    DateTimeOffset Timestamp,
    /// <summary>Portfolio state at the time of this bar.</summary>
    PortfolioSnapshot Snapshot);
