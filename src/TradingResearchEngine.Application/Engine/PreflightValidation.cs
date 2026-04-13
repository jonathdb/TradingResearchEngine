namespace TradingResearchEngine.Application.Engine;

/// <summary>Severity level for a preflight validation finding.</summary>
public enum PreflightSeverity
{
    /// <summary>Blocks execution — the run cannot proceed.</summary>
    Error,
    /// <summary>Run proceeds but the finding should be reviewed.</summary>
    Warning,
    /// <summary>Informational suggestion for improved research quality.</summary>
    Recommendation
}

/// <summary>A single structured finding from preflight validation.</summary>
/// <param name="Field">The configuration field that triggered the finding.</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="Severity">How severe the finding is.</param>
/// <param name="Code">Machine-readable code (e.g. "MISSING_PARAM", "RANGE_VIOLATION").</param>
public sealed record PreflightFinding(
    string Field,
    string Message,
    PreflightSeverity Severity,
    string Code);

/// <summary>Result of preflight validation containing all findings.</summary>
/// <param name="Findings">All findings produced by the validator.</param>
public sealed record PreflightResult(
    IReadOnlyList<PreflightFinding> Findings)
{
    /// <summary>True when at least one finding has <see cref="PreflightSeverity.Error"/> severity.</summary>
    public bool HasErrors => Findings.Any(f => f.Severity == PreflightSeverity.Error);

    /// <summary>Number of findings with <see cref="PreflightSeverity.Error"/> severity.</summary>
    public int ErrorCount => Findings.Count(f => f.Severity == PreflightSeverity.Error);

    /// <summary>Number of findings with <see cref="PreflightSeverity.Warning"/> severity.</summary>
    public int WarningCount => Findings.Count(f => f.Severity == PreflightSeverity.Warning);
}
