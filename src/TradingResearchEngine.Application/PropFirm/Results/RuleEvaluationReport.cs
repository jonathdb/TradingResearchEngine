namespace TradingResearchEngine.Application.PropFirm.Results;

/// <summary>Result of evaluating a BacktestResult against a FirmRuleSet.</summary>
public sealed record RuleEvaluationReport(
    bool Passed,
    string ChallengeOutcome,
    IReadOnlyList<string> ViolatedRules);
