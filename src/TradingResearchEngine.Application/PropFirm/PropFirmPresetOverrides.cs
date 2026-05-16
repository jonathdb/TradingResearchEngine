namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Typed overrides for a user-defined prop-firm variance scenario.
/// All fields are optional — null values fall back to the base configuration.
/// </summary>
/// <param name="GrossMonthlyReturnPercent">Override for gross monthly return percentage. Null uses base config value.</param>
/// <param name="PayoutFrictionFactor">Override for payout friction factor. Null uses base config value.</param>
/// <param name="PassRatePercent">Override for pass rate / direct-funded probability percentage. Null uses base config value.</param>
public sealed record PropFirmPresetOverrides(
    decimal? GrossMonthlyReturnPercent = null,
    decimal? PayoutFrictionFactor = null,
    decimal? PassRatePercent = null);
