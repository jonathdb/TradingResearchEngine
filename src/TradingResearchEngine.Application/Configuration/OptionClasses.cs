namespace TradingResearchEngine.Application.Configuration;

/// <summary>Named defaults — no magic numbers in engine logic.</summary>
public static class MonteCarloDefaults
{
    /// <summary>Default number of Monte Carlo simulation paths.</summary>
    public const int DefaultSimulationCount = 1000;
}

/// <summary>Named defaults for risk layer configuration.</summary>
public static class RiskDefaults
{
    /// <summary>Default maximum portfolio exposure as a percentage of total equity.</summary>
    public const decimal MaxExposurePercent = 10m;
}

/// <summary>Named defaults for reporting.</summary>
public static class ReportingDefaults
{
    /// <summary>Default number of decimal places for monetary and percentage values.</summary>
    public const int DecimalPlaces = 2;
}

/// <summary>Options for Monte Carlo simulation workflow.</summary>
public sealed class MonteCarloOptions
{
    /// <summary>Number of simulation paths to run. Minimum 1.</summary>
    public int SimulationCount { get; set; } = MonteCarloDefaults.DefaultSimulationCount;

    /// <summary>Optional RNG seed for reproducible results.</summary>
    public int? Seed { get; set; }

    /// <summary>Equity drawdown fraction at which a path is classified as ruin.</summary>
    public decimal RuinThresholdPercent { get; set; } = 0.5m;
}

/// <summary>Options for the risk layer.</summary>
public sealed class RiskOptions
{
    /// <summary>Maximum open exposure as a percentage of total equity.</summary>
    public decimal MaxExposurePercent { get; set; } = RiskDefaults.MaxExposurePercent;
}

/// <summary>Options for reporters.</summary>
public sealed class ReportingOptions
{
    /// <summary>Decimal places for monetary and percentage values in rendered output.</summary>
    public int DecimalPlaces { get; set; } = ReportingDefaults.DecimalPlaces;
}

/// <summary>Options for the JSON file repository.</summary>
public sealed class RepositoryOptions
{
    /// <summary>Base directory where JSON entity files are stored.</summary>
    public string BaseDirectory { get; set; } = string.Empty;
}

/// <summary>Options for parameter sweep parallelism.</summary>
public sealed class SweepOptions
{
    /// <summary>Maximum number of concurrent engine runs during a sweep.</summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
}

/// <summary>Options for walk-forward analysis windowing.</summary>
public sealed class WalkForwardOptions
{
    /// <summary>Length of the in-sample window (used as initial IS length in Anchored mode).</summary>
    public TimeSpan InSampleLength { get; set; }

    /// <summary>Length of the out-of-sample window.</summary>
    public TimeSpan OutOfSampleLength { get; set; }

    /// <summary>Step size between consecutive windows.</summary>
    public TimeSpan StepSize { get; set; }

    /// <summary>When true, the in-sample window always starts at the data origin (anchored). Deprecated: use <see cref="Mode"/> instead.</summary>
    public bool AnchoredWindow { get; set; }

    /// <summary>V4: Walk-forward mode. When set, overrides <see cref="AnchoredWindow"/>.</summary>
    public TradingResearchEngine.Application.Research.WalkForwardMode? Mode { get; set; }

    /// <summary>Resolved mode: prefers <see cref="Mode"/> if set, falls back to <see cref="AnchoredWindow"/>.</summary>
    public TradingResearchEngine.Application.Research.WalkForwardMode EffectiveMode =>
        Mode ?? (AnchoredWindow
            ? TradingResearchEngine.Application.Research.WalkForwardMode.Anchored
            : TradingResearchEngine.Application.Research.WalkForwardMode.Rolling);
}
