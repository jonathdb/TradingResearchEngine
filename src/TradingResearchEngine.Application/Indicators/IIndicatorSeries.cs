using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Streaming, warm-up-aware indicator computation interface.
/// Implementations accept bars one at a time and maintain a running result series.
/// </summary>
/// <typeparam name="TResult">The indicator result type (e.g., SmaResult, EmaResult).</typeparam>
public interface IIndicatorSeries<TResult>
{
    /// <summary>
    /// Adds a new bar and recomputes the indicator.
    /// Results are appended in chronological order.
    /// </summary>
    /// <param name="bar">The OHLCV bar record to process.</param>
    void Add(BarRecord bar);

    /// <summary>
    /// Resets the indicator to its initial state, clearing all results and internal buffers.
    /// </summary>
    void Reset();

    /// <summary>
    /// All computed results in chronological order.
    /// </summary>
    IReadOnlyList<TResult> Results { get; }

    /// <summary>
    /// True when enough bars have been added for valid computation
    /// (i.e., Results.Count is greater than or equal to the warm-up period).
    /// </summary>
    bool IsWarm { get; }
}
