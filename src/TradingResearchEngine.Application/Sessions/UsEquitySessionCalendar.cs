using TradingResearchEngine.Core.Sessions;

namespace TradingResearchEngine.Application.Sessions;

/// <summary>
/// US equity session calendar (Eastern Time).
/// Pre-market: 04:00–09:30 ET, Regular: 09:30–16:00 ET, After-hours: 16:00–20:00 ET.
/// Weekends and US market holidays are not tradable.
/// </summary>
public sealed class UsEquitySessionCalendar : ISessionCalendar
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static readonly TradingSession PreMarket = new("PreMarket", new TimeOnly(4, 0), new TimeOnly(9, 30), "Eastern Standard Time");
    private static readonly TradingSession Regular = new("Regular", new TimeOnly(9, 30), new TimeOnly(16, 0), "Eastern Standard Time");
    private static readonly TradingSession AfterHours = new("AfterHours", new TimeOnly(16, 0), new TimeOnly(20, 0), "Eastern Standard Time");

    /// <inheritdoc/>
    public IReadOnlyList<TradingSession> Sessions { get; } = new[] { PreMarket, Regular, AfterHours };

    /// <inheritdoc/>
    public bool IsTradable(DateTimeOffset timestamp)
    {
        var et = TimeZoneInfo.ConvertTime(timestamp, Eastern);
        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;

        var time = TimeOnly.FromDateTime(et.DateTime);
        return time >= PreMarket.Start && time < AfterHours.End;
    }

    /// <inheritdoc/>
    public string ClassifySession(DateTimeOffset timestamp)
    {
        if (!IsTradable(timestamp)) return "Closed";

        var et = TimeZoneInfo.ConvertTime(timestamp, Eastern);
        var time = TimeOnly.FromDateTime(et.DateTime);

        if (time >= Regular.Start && time < Regular.End) return "Regular";
        if (time >= PreMarket.Start && time < PreMarket.End) return "PreMarket";
        if (time >= AfterHours.Start && time < AfterHours.End) return "AfterHours";

        return "Closed";
    }
}
