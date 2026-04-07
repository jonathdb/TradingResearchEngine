namespace TradingResearchEngine.Application.PropFirm.Results;

/// <summary>Result of prop-firm variance analysis across presets.</summary>
public sealed record PropFirmVarianceResult(
    IReadOnlyList<PropFirmScenarioResult> Variants);
