namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of sensitivity analysis across cost and delay perturbations.</summary>
public sealed record SensitivityResult(
    IReadOnlyList<SensitivityRow> Matrix,
    decimal CostSensitivity,
    decimal DelaySensitivity,
    decimal ExecutionRobustnessScore);

/// <summary>A single perturbation result row.</summary>
public sealed record SensitivityRow(
    string PerturbationName,
    decimal CAGR,
    decimal? Sharpe,
    decimal MaxDrawdown,
    decimal? ProfitFactor);

/// <summary>Options for sensitivity analysis.</summary>
public sealed class SensitivityOptions
{
    /// <summary>Spread multipliers to test (default: 1.25, 1.5, 2.0).</summary>
    public IReadOnlyList<decimal> SpreadMultipliers { get; set; } = new[] { 1.25m, 1.5m, 2.0m };

    /// <summary>Slippage multipliers to test (default: 1.5, 2.0).</summary>
    public IReadOnlyList<decimal> SlippageMultipliers { get; set; } = new[] { 1.5m, 2.0m };

    /// <summary>Whether to test one-bar entry delay (default true).</summary>
    public bool TestEntryDelay { get; set; } = true;

    /// <summary>Whether to test one-bar exit delay (default true).</summary>
    public bool TestExitDelay { get; set; } = true;

    /// <summary>Position sizing multiplier for reduced sizing test (default 0.5).</summary>
    public decimal SizingMultiplier { get; set; } = 0.5m;
}
