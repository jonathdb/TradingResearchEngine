namespace TradingResearchEngine.Application.PropFirm.Results;

/// <summary>Economics result for a single prop-firm scenario.</summary>
public sealed record PropFirmScenarioResult(
    string PresetName,
    decimal ChallengeProbability,
    decimal MonthlyPayoutExpectancy,
    decimal LifetimeEV,
    int? BreakevenMonths);
