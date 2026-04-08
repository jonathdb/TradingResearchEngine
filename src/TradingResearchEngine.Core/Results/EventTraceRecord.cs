namespace TradingResearchEngine.Core.Results;

/// <summary>
/// A single entry in the event trace log. Recorded only when trace mode is enabled.
/// </summary>
public sealed record EventTraceRecord(
    DateTimeOffset Timestamp,
    string EventType,
    string Symbol,
    string Description,
    Dictionary<string, object>? Details = null);
