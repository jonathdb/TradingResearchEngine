namespace TradingResearchEngine.Application.Configuration;

/// <summary>Options controlling sweep UI warnings and visual indicators.</summary>
public sealed class SweepUiOptions
{
    /// <summary>
    /// Total combination count threshold above which an overfitting warning is displayed
    /// when any parameter dimension has High sensitivity.
    /// Default: <see cref="SweepUiDefaults.CombinationWarningThreshold"/>.
    /// </summary>
    public int CombinationWarningThreshold { get; set; } = SweepUiDefaults.CombinationWarningThreshold;
}

/// <summary>Named default constants for sweep UI configuration.</summary>
public static class SweepUiDefaults
{
    /// <summary>Default combination warning threshold (1,000).</summary>
    public const int CombinationWarningThreshold = 1000;
}
