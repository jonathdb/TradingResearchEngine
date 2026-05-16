namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Distinguishes the data source mode for paper trading sessions.
/// </summary>
public enum DataFeedMode
{
    /// <summary>
    /// Simulated playback of historical data. Bars are replayed from a stored data source.
    /// </summary>
    Replay,

    /// <summary>
    /// Live data feed from a real broker or market data provider via polling.
    /// </summary>
    Live
}
