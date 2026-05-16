namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration options for the research checklist service.
/// Bound from appsettings.json section "ResearchChecklist".
/// </summary>
public sealed class ResearchChecklistOptions
{
    /// <summary>Configuration section name for IOptions binding.</summary>
    public const string SectionName = "ResearchChecklist";

    /// <summary>
    /// Minimum Deflated Sharpe Ratio threshold. Results below this value
    /// indicate potential multiple-testing bias.
    /// </summary>
    public decimal MinDsrThreshold { get; set; } = ResearchChecklistDefaults.MinDsrThreshold;
}

/// <summary>Named constants for research checklist configuration defaults.</summary>
public static class ResearchChecklistDefaults
{
    /// <summary>Default minimum DSR threshold (0.5).</summary>
    public const decimal MinDsrThreshold = 0.5m;
}
