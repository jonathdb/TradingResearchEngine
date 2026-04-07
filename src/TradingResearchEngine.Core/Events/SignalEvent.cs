namespace TradingResearchEngine.Core.Events;

/// <summary>A directional signal produced by a strategy, optionally carrying a strength value.</summary>
public record SignalEvent(
    string Symbol,
    Direction Direction,
    decimal? Strength,
    DateTimeOffset Timestamp)
    : EngineEvent(Timestamp);
