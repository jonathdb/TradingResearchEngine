using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Strategy.Composite;

/// <summary>
/// Wraps an <see cref="Indicators.IIndicatorSeries{TResult}"/> with its config ID
/// and provides typed value extraction for use in condition evaluation.
/// </summary>
public interface IIndicatorInstance
{
    /// <summary>Gets the unique identifier for this indicator instance.</summary>
    string Id { get; }

    /// <summary>Gets the indicator type (e.g., "sma", "rsi", "macd").</summary>
    string Type { get; }

    /// <summary>Gets whether the indicator has received enough bars for valid computation.</summary>
    bool IsWarm { get; }

    /// <summary>
    /// Adds a new bar and recomputes the indicator.
    /// </summary>
    /// <param name="bar">The OHLCV bar record to process.</param>
    void Add(BarRecord bar);

    /// <summary>
    /// Resets the indicator to its initial state, clearing all results and internal buffers.
    /// </summary>
    void Reset();

    /// <summary>Gets the current primary value (e.g., SMA value, RSI value).</summary>
    decimal? CurrentValue { get; }

    /// <summary>Gets the previous primary value (for cross detection).</summary>
    decimal? PreviousValue { get; }

    /// <summary>
    /// Gets a sub-property value (e.g., "Signal" for MACD, "Upper" for Bollinger).
    /// </summary>
    /// <param name="subProperty">The sub-property name to retrieve.</param>
    /// <returns>The sub-property value, or null if unavailable.</returns>
    decimal? GetSubValue(string subProperty);

    /// <summary>
    /// Gets the previous sub-property value (for cross detection on sub-properties).
    /// </summary>
    /// <param name="subProperty">The sub-property name to retrieve.</param>
    /// <returns>The previous sub-property value, or null if unavailable.</returns>
    decimal? GetPreviousSubValue(string subProperty);
}
