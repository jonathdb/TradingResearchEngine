using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Bollinger Bands indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: Middle Band = SMA(N); Upper Band = Middle + (k × σ); Lower Band = Middle - (k × σ),
/// where σ is the standard deviation of closing prices over N periods and k is the multiplier.
/// </para>
/// <para>
/// Typical use: Volatility measurement and mean-reversion signals. Prices touching the upper
/// band suggest overbought conditions; prices touching the lower band suggest oversold.
/// Band squeeze (narrowing) often precedes breakout moves.
/// Common parameters: period=20, standardDeviations=2.
/// </para>
/// </remarks>
public sealed class BollingerBandsIndicator : SkenderIndicatorAdapter<BollingerBandsResult>
{
    private readonly int _period;
    private readonly double _standardDeviations;

    /// <summary>
    /// Initializes a new Bollinger Bands indicator with the specified parameters.
    /// </summary>
    /// <param name="period">The SMA lookback period (typically 20).</param>
    /// <param name="standardDeviations">The band width multiplier (typically 2.0).</param>
    public BollingerBandsIndicator(int period = 20, double standardDeviations = 2.0)
    {
        _period = period;
        _standardDeviations = standardDeviations;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period;

    /// <inheritdoc />
    protected override IReadOnlyList<BollingerBandsResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetBollingerBands(_period, _standardDeviations).ToList();
    }
}
