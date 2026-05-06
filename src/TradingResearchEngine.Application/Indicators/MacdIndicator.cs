using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Moving Average Convergence Divergence (MACD) indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: MACD Line = EMA(fast) - EMA(slow); Signal Line = EMA(MACD Line, signal period);
/// Histogram = MACD Line - Signal Line.
/// </para>
/// <para>
/// Typical use: Trend direction and momentum measurement. Buy signals occur when the MACD
/// crosses above the signal line; sell signals when it crosses below. The histogram shows
/// the strength of the trend. Common parameters: fast=12, slow=26, signal=9.
/// </para>
/// </remarks>
public sealed class MacdIndicator : SkenderIndicatorAdapter<MacdResult>
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly int _signalPeriod;

    /// <summary>
    /// Initializes a new MACD indicator with the specified parameters.
    /// </summary>
    /// <param name="fastPeriod">The fast EMA period (typically 12).</param>
    /// <param name="slowPeriod">The slow EMA period (typically 26).</param>
    /// <param name="signalPeriod">The signal line EMA period (typically 9).</param>
    public MacdIndicator(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _signalPeriod = signalPeriod;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _slowPeriod + _signalPeriod - 1;

    /// <inheritdoc />
    protected override IReadOnlyList<MacdResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetMacd(_fastPeriod, _slowPeriod, _signalPeriod).ToList();
    }
}
