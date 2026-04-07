namespace TradingResearchEngine.Core.Events;

/// <summary>
/// A concrete order to be routed through the RiskLayer to the ExecutionHandler.
/// <see cref="RiskApproved"/> is set to <c>true</c> only by the RiskLayer after evaluation.
/// </summary>
public record OrderEvent(
    string Symbol,
    Direction Direction,
    decimal Quantity,
    OrderType OrderType,
    decimal? LimitPrice,
    DateTimeOffset Timestamp,
    bool RiskApproved = false)
    : EngineEvent(Timestamp);
