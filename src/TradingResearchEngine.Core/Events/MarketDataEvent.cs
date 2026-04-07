namespace TradingResearchEngine.Core.Events;

/// <summary>Base record for all market data events (bar or tick).</summary>
public abstract record MarketDataEvent(string Symbol, DateTimeOffset Timestamp)
    : EngineEvent(Timestamp);
