using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Relative Strength Index (RSI) indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: RSI = 100 - (100 / (1 + RS)), where RS = Average Gain / Average Loss over N periods.
/// </para>
/// <para>
/// Typical use: Identifying overbought (RSI &gt; 70) and oversold (RSI &lt; 30) conditions.
/// Also used for divergence detection and trend confirmation.
/// Common period: 14.
/// </para>
/// </remarks>
public sealed class RsiIndicator : SkenderIndicatorAdapter<RsiResult>
{
    private readonly int _period;

    /// <summary>
    /// Initializes a new RSI indicator with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars used to compute the RSI (typically 14).</param>
    public RsiIndicator(int period)
    {
        _period = period;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period + 1;

    /// <inheritdoc />
    protected override IReadOnlyList<RsiResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetRsi(_period).ToList();
    }
}
