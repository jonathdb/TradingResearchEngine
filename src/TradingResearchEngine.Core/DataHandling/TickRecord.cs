using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.DataHandling;

/// <summary>An immutable tick record carrying order-book depth and last-trade data.</summary>
public sealed record TickRecord(
    string Symbol,
    IReadOnlyList<BidLevel> BidLevels,
    IReadOnlyList<AskLevel> AskLevels,
    LastTrade LastTrade,
    DateTimeOffset Timestamp);
