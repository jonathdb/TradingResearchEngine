namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Configuration options for comparison report generation.
/// Bound from <c>appsettings.json:Reports:Comparison</c>.
/// </summary>
public sealed class ComparisonReportOptions
{
    /// <summary>Output directory for generated comparison reports. Defaults to "reports".</summary>
    public string OutputDirectory { get; set; } = "reports";

    /// <summary>When true, generates an HTML report in addition to the Markdown artifact.</summary>
    public bool EnableHtml { get; set; }
}
