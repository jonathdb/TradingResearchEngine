namespace TradingResearchEngine.Application.PropFirm.Results;

/// <summary>Evaluation result for a single challenge phase.</summary>
public sealed record PhaseEvaluationResult(
    string PhaseName,
    bool Passed,
    IReadOnlyList<RuleResult> Rules);

/// <summary>Result of evaluating a single rule within a challenge phase.</summary>
public sealed record RuleResult(
    string RuleName,
    RuleStatus Status,
    decimal ActualValue,
    decimal LimitValue,
    decimal Margin);

/// <summary>Status of a rule evaluation.</summary>
public enum RuleStatus
{
    /// <summary>Rule passed with comfortable margin.</summary>
    Passed,
    /// <summary>Rule passed but within 20% of the limit.</summary>
    NearBreach,
    /// <summary>Rule violated.</summary>
    Failed
}
