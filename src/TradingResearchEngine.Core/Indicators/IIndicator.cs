namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// A stateful, composable technical indicator that processes price data
/// one bar at a time and produces a typed output value.
/// </summary>
/// <typeparam name="TOutput">The indicator's output type (value type or reference type).</typeparam>
public interface IIndicator<TOutput> where TOutput : struct
{
    /// <summary>Current output value. Null until the indicator has received enough data.</summary>
    TOutput? Value { get; }

    /// <summary>True when the indicator has received enough data to produce a valid output.</summary>
    bool IsReady { get; }

    /// <summary>Feeds a new price bar to the indicator.</summary>
    /// <param name="close">The closing price of the bar.</param>
    /// <param name="high">The high price of the bar (required by some indicators).</param>
    /// <param name="low">The low price of the bar (required by some indicators).</param>
    void Update(decimal close, decimal? high = null, decimal? low = null);

    /// <summary>Resets the indicator to its initial state.</summary>
    void Reset();
}
