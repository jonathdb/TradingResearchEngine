namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Output record for the <see cref="DonchianChannel"/> indicator containing
/// upper channel (highest high), lower channel (lowest low), and middle values.
/// </summary>
/// <param name="Upper">The highest high over the lookback period.</param>
/// <param name="Lower">The lowest low over the lookback period.</param>
/// <param name="Middle">The midpoint: (Upper + Lower) / 2.</param>
public readonly record struct DonchianChannelOutput(decimal Upper, decimal Lower, decimal Middle);
