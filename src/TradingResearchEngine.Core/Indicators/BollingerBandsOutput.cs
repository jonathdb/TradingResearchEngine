namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Output record for the <see cref="BollingerBands"/> indicator containing
/// upper band, middle band (SMA), lower band, and bandwidth values.
/// </summary>
/// <param name="Upper">The upper Bollinger Band (Middle + K * StdDev).</param>
/// <param name="Middle">The middle band (simple moving average).</param>
/// <param name="Lower">The lower Bollinger Band (Middle - K * StdDev).</param>
/// <param name="BandWidth">The normalised bandwidth: (Upper - Lower) / Middle.</param>
public readonly record struct BollingerBandsOutput(decimal Upper, decimal Middle, decimal Lower, decimal BandWidth);
