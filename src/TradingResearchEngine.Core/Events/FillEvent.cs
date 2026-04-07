namespace TradingResearchEngine.Core.Events;

/// <summary>A confirmed execution produced by the ExecutionHandler.</summary>
public record FillEvent(
    string Symbol,
    Direction Direction,
    decimal Quantity,
    decimal FillPrice,
    decimal Commission,
    decimal SlippageAmount,
    DateTimeOffset Timestamp)
    : EngineEvent(Timestamp);
