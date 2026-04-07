namespace TradingResearchEngine.Core.Events;

/// <summary>Base record for all events flowing through the engine's EventQueue.</summary>
public abstract record EngineEvent(DateTimeOffset Timestamp);
