using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Stochastic Oscillator indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: %K = 100 × (Close - Lowest Low(N)) / (Highest High(N) - Lowest Low(N));
/// %D = SMA(%K, signal period). Smoothed variant applies additional SMA to %K.
/// </para>
/// <para>
/// Typical use: Identifying overbought (%K &gt; 80) and oversold (%K &lt; 20) conditions.
/// Crossovers between %K and %D generate trade signals. Divergence between price and
/// stochastic can signal trend reversals.
/// Common parameters: lookbackPeriod=14, signalPeriod=3, smoothPeriod=3.
/// </para>
/// </remarks>
public sealed class StochasticIndicator : SkenderIndicatorAdapter<StochResult>
{
    private readonly int _lookbackPeriod;
    private readonly int _signalPeriod;
    private readonly int _smoothPeriod;

    /// <summary>
    /// Initializes a new Stochastic Oscillator indicator with the specified parameters.
    /// </summary>
    /// <param name="lookbackPeriod">The %K lookback period (typically 14).</param>
    /// <param name="signalPeriod">The %D signal smoothing period (typically 3).</param>
    /// <param name="smoothPeriod">The %K smoothing period (typically 3).</param>
    public StochasticIndicator(int lookbackPeriod = 14, int signalPeriod = 3, int smoothPeriod = 3)
    {
        _lookbackPeriod = lookbackPeriod;
        _signalPeriod = signalPeriod;
        _smoothPeriod = smoothPeriod;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _lookbackPeriod + _signalPeriod - 1;

    /// <inheritdoc />
    protected override IReadOnlyList<StochResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetStoch(_lookbackPeriod, _signalPeriod, _smoothPeriod).ToList();
    }
}
