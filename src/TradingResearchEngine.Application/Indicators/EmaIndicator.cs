using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Exponential Moving Average (EMA) indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: EMA = Close × k + EMA(previous) × (1 - k), where k = 2 / (N + 1).
/// </para>
/// <para>
/// Typical use: Trend-following and crossover strategies. The EMA gives more weight
/// to recent prices than the SMA, making it more responsive to new information.
/// Commonly used periods: 12 and 26 (MACD components), 9 (short-term), 50 and 200 (long-term).
/// </para>
/// </remarks>
public sealed class EmaIndicator : SkenderIndicatorAdapter<EmaResult>
{
    private readonly int _period;

    /// <summary>
    /// Initializes a new EMA indicator with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars used to compute the exponential average.</param>
    public EmaIndicator(int period)
    {
        _period = period;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period;

    /// <inheritdoc />
    protected override IReadOnlyList<EmaResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetEma(_period).ToList();
    }
}
