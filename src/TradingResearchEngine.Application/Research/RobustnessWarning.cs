namespace TradingResearchEngine.Application.Research;

/// <summary>Severity level for robustness warnings.</summary>
public enum RobustnessSeverity
{
    /// <summary>Critical issue requiring immediate attention.</summary>
    Critical,
    /// <summary>High-severity issue likely indicating overfitting or unreliable results.</summary>
    High,
    /// <summary>Medium-severity issue that warrants investigation.</summary>
    Medium,
    /// <summary>Low-severity advisory for awareness.</summary>
    Low
}

/// <summary>
/// A structured robustness warning with severity, explanation, and recommended action.
/// Replaces the plain string warnings from <see cref="IRobustnessAdvisoryService.GetWarnings"/>.
/// </summary>
/// <param name="Severity">Warning severity level.</param>
/// <param name="Code">Machine-readable warning code (e.g., "HIGH_SHARPE", "LOW_TRADES").</param>
/// <param name="Explanation">Human-readable explanation of the warning.</param>
/// <param name="RecommendedAction">Suggested next step to address the warning.</param>
/// <param name="Cause">Root cause explanation for why this metric is suspicious.</param>
/// <param name="Remediation">Specific remediation steps.</param>
/// <param name="CauseCategory">Category grouping for related warnings (e.g., "Overfitting", "InsufficientData").</param>
public sealed record RobustnessWarning(
    RobustnessSeverity Severity,
    string Code,
    string Explanation,
    string RecommendedAction,
    string? Cause = null,
    string? Remediation = null,
    string? CauseCategory = null);
