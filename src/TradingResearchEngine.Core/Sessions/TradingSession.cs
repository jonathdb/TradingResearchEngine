namespace TradingResearchEngine.Core.Sessions;

/// <summary>A named trading session window with start/end times and timezone.</summary>
public readonly record struct TradingSession(
    string Name,
    TimeOnly Start,
    TimeOnly End,
    string TimeZoneId);
