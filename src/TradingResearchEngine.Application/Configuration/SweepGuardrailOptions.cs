namespace TradingResearchEngine.Application.Configuration;

/// <summary>Options controlling parameter sweep execution guardrails.</summary>
public sealed class SweepGuardrailOptions
{
    /// <summary>
    /// Maximum number of parameter combinations allowed before execution is rejected.
    /// Default: <see cref="SweepGuardrailDefaults.MaxCombinations"/>.
    /// </summary>
    public int MaxCombinations { get; set; } = SweepGuardrailDefaults.MaxCombinations;
}

/// <summary>Named default constants for sweep guardrail configuration.</summary>
public static class SweepGuardrailDefaults
{
    /// <summary>Default maximum parameter combinations (10,000).</summary>
    public const int MaxCombinations = 10_000;
}
