using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Simple Moving Average (SMA) indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: SMA = (Sum of closing prices over N periods) / N
/// </para>
/// <para>
/// Typical use: Trend identification and support/resistance levels.
/// A rising SMA indicates an uptrend; a falling SMA indicates a downtrend.
/// Commonly used periods: 20 (short-term), 50 (medium-term), 200 (long-term).
/// </para>
/// </remarks>
public sealed class SmaIndicator : SkenderIndicatorAdapter<SmaResult>
{
    private readonly int _period;

    /// <summary>
    /// Initializes a new SMA indicator with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars used to compute the average.</param>
    public SmaIndicator(int period)
    {
        _period = period;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period;

    /// <inheritdoc />
    protected override IReadOnlyList<SmaResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetSma(_period).ToList();
    }
}
