using TradingResearchEngine.Application.Configuration;

namespace TradingResearchEngine.Application.PaperTrading;

/// <summary>
/// Exposes observable metrics from a live data feed provider for UI consumption.
/// </summary>
public interface IDataFeedMetrics
{
    /// <summary>
    /// The timestamp of the last successful poll from the data feed endpoint.
    /// Null if no successful poll has occurred yet.
    /// </summary>
    DateTimeOffset? LastSuccessfulPoll { get; }

    /// <summary>
    /// The number of consecutive poll failures since the last successful poll.
    /// Resets to zero on each successful poll.
    /// </summary>
    int ConsecutiveFailureCount { get; }

    /// <summary>
    /// The current data feed mode the provider is operating in.
    /// </summary>
    DataFeedMode CurrentMode { get; }
}
