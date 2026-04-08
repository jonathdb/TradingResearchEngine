using TradingResearchEngine.Core.Sessions;

namespace TradingResearchEngine.Application.Sessions;

/// <summary>
/// Forex 24-hour session calendar (Sunday 17:00 ET to Friday 17:00 ET).
/// Sessions: Asia (00:00–09:00 UTC), London (07:00–16:00 UTC),
/// NewYork (12:00–21:00 UTC), Overlap (12:00–16:00 UTC).
/// Weekend hours are not tradable.
/// </summary>
public sealed class ForexSessionCalendar : ISessionCalendar
{
    private static readonly TradingSession Asia = new("Asia", new TimeOnly(0, 0), new TimeOnly(9, 0), "UTC");
    private static readonly TradingSession London = new("London", new TimeOnly(7, 0), new TimeOnly(16, 0), "UTC");
    private static readonly TradingSession NewYork = new("NewYork", new TimeOnly(12, 0), new TimeOnly(21, 0), "UTC");
    private static readonly TradingSession Overlap = new("Overlap", new TimeOnly(12, 0), new TimeOnly(16, 0), "UTC");

    /// <inheritdoc/>
    public IReadOnlyList<TradingSession> Sessions { get; } = new[] { Asia, London, NewYork, Overlap };

    /// <inheritdoc/>
    public bool IsTradable(DateTimeOffset timestamp)
    {
        var utc = timestamp.UtcDateTime;
        // Forex is closed Saturday and Sunday (simplified: closed Sat 00:00 to Sun 21:00 UTC)
        return utc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <inheritdoc/>
    public string ClassifySession(DateTimeOffset timestamp)
    {
        if (!IsTradable(timestamp)) return "Closed";

        var time = TimeOnly.FromDateTime(timestamp.UtcDateTime);

        bool inLondon = time >= London.Start && time < London.End;
        bool inNewYork = time >= NewYork.Start && time < NewYork.End;

        if (inLondon && inNewYork) return "Overlap";
        if (inLondon) return "London";
        if (inNewYork) return "NewYork";
        if (time >= Asia.Start && time < Asia.End) return "Asia";

        return "AfterHours";
    }
}
