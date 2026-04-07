namespace TradingResearchEngine.Core.Portfolio;

/// <summary>Represents an open position in a single instrument.</summary>
public sealed record Position(
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal UnrealisedPnl,
    decimal RealisedPnl);
