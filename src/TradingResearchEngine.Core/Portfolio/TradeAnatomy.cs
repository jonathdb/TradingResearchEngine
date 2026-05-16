namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Intra-trade analytics for a completed round-trip trade.
/// MAE and MFE are expressed as fractions of entry value (e.g., -0.05 = 5% adverse excursion).
/// </summary>
/// <param name="MaxAdverseExcursion">
/// The worst unrealised P&amp;L during the trade as a fraction of entry value.
/// Null when intra-trade price data is unavailable.
/// </param>
/// <param name="MaxFavorableExcursion">
/// The best unrealised P&amp;L during the trade as a fraction of entry value.
/// Null when intra-trade price data is unavailable.
/// </param>
/// <param name="Duration">Time elapsed between trade entry and exit.</param>
public sealed record TradeAnatomy(
    decimal? MaxAdverseExcursion,
    decimal? MaxFavorableExcursion,
    TimeSpan Duration);
