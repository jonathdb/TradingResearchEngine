using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Average True Range (ATR) indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: TR = max(High - Low, |High - Previous Close|, |Low - Previous Close|);
/// ATR = Smoothed average of TR over N periods (Wilder's smoothing).
/// </para>
/// <para>
/// Typical use: Volatility measurement for position sizing, stop-loss placement, and
/// breakout confirmation. Higher ATR indicates higher volatility. Commonly used for
/// trailing stops (e.g., 2× ATR) and volatility-scaled position sizing.
/// Common period: 14.
/// </para>
/// </remarks>
public sealed class AtrIndicator : SkenderIndicatorAdapter<AtrResult>
{
    private readonly int _period;

    /// <summary>
    /// Initializes a new ATR indicator with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars used to compute the ATR (typically 14).</param>
    public AtrIndicator(int period)
    {
        _period = period;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period + 1;

    /// <inheritdoc />
    protected override IReadOnlyList<AtrResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetAtr(_period).ToList();
    }
}
