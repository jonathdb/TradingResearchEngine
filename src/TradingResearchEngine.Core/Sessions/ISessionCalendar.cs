namespace TradingResearchEngine.Core.Sessions;

/// <summary>
/// Classifies timestamps into trading sessions. Used by the engine, risk layer,
/// and slippage models. Strategies must not reference this interface directly.
/// </summary>
public interface ISessionCalendar
{
    /// <summary>Returns true if the timestamp falls within a tradable session.</summary>
    bool IsTradable(DateTimeOffset timestamp);

    /// <summary>
    /// Returns the session bucket name for the timestamp
    /// (e.g. "Asia", "London", "NewYork", "Overlap", "AfterHours", "Closed").
    /// </summary>
    string ClassifySession(DateTimeOffset timestamp);

    /// <summary>All defined sessions in this calendar.</summary>
    IReadOnlyList<TradingSession> Sessions { get; }
}
