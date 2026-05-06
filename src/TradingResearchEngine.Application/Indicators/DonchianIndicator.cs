using Skender.Stock.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Donchian Channel indicator wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Formula: Upper Channel = Highest High over N periods; Lower Channel = Lowest Low over N periods;
/// Middle = (Upper + Lower) / 2.
/// </para>
/// <para>
/// Typical use: Breakout trading and trend identification. A new high above the upper channel
/// signals a potential long entry; a new low below the lower channel signals a potential short
/// entry. The channel width indicates volatility. Used in the classic Turtle Trading system.
/// Common period: 20.
/// </para>
/// </remarks>
public sealed class DonchianIndicator : SkenderIndicatorAdapter<DonchianResult>
{
    private readonly int _period;

    /// <summary>
    /// Initializes a new Donchian Channel indicator with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars used to compute the channel (typically 20).</param>
    public DonchianIndicator(int period)
    {
        _period = period;
    }

    /// <inheritdoc />
    protected override int WarmupPeriod => _period;

    /// <inheritdoc />
    protected override IReadOnlyList<DonchianResult> Compute(IReadOnlyList<Quote> quotes)
    {
        return quotes.GetDonchian(_period).ToList();
    }
}
