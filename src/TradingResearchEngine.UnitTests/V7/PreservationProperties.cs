using FsCheck;
using FsCheck.Xunit;
using System.Text.RegularExpressions;

namespace TradingResearchEngine.UnitTests.V7;

/// <summary>
/// Feature: v7-bugfix-pass, Property 2: Preservation
/// Tests that verify existing behavior that must NOT regress after the fix.
/// These tests MUST PASS on unfixed code to confirm baseline behavior to preserve.
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public class PreservationProperties
{
    /// <summary>
    /// Resolves the path to the source file relative to the test execution directory.
    /// Walks up from bin/Debug/net8.0 to the repo root, then navigates to the source file.
    /// </summary>
    private static string GetSourceFilePath(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (dir is null)
            throw new InvalidOperationException("Could not find repository root from test execution directory.");

        return Path.Combine(dir, relativePath);
    }

    private static string ReadResultDetailRazor()
    {
        var path = GetSourceFilePath("src/TradingResearchEngine.Web/Components/Pages/Backtests/ResultDetail.razor");
        return File.ReadAllText(path);
    }

    private static string ReadHistoryRazor()
    {
        var path = GetSourceFilePath("src/TradingResearchEngine.Web/Components/Pages/Backtests/History.razor");
        return File.ReadAllText(path);
    }

    // ========================================================================
    // Preservation 1 — "Charts" tab contains all required chart components
    // ========================================================================

    /// <summary>
    /// The "Charts" tab SHALL contain an EquityCurveChart component.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartsTab_ContainsEquityCurveChart(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartsTabContent = ExtractChartsTabPanel(content);
        if (chartsTabContent is null) return false;

        return chartsTabContent.Contains("EquityCurveChart");
    }

    /// <summary>
    /// The "Charts" tab SHALL contain a MonthlyReturnsHeatmap component.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartsTab_ContainsMonthlyReturnsHeatmap(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartsTabContent = ExtractChartsTabPanel(content);
        if (chartsTabContent is null) return false;

        return chartsTabContent.Contains("MonthlyReturnsHeatmap");
    }

    /// <summary>
    /// The "Charts" tab SHALL contain a TradePnlHistogram component.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartsTab_ContainsTradePnlHistogram(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartsTabContent = ExtractChartsTabPanel(content);
        if (chartsTabContent is null) return false;

        return chartsTabContent.Contains("TradePnlHistogram");
    }

    /// <summary>
    /// The "Charts" tab SHALL contain a HoldingPeriodHistogram component.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartsTab_ContainsHoldingPeriodHistogram(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartsTabContent = ExtractChartsTabPanel(content);
        if (chartsTabContent is null) return false;

        return chartsTabContent.Contains("HoldingPeriodHistogram");
    }

    // ========================================================================
    // Preservation 2 — "Trades", "P&L", "Config" tabs remain present
    // ========================================================================

    /// <summary>
    /// The chart tabs section SHALL contain a "Trades" tab panel.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_TradesTabPresent(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartTabsSection = ExtractLastMudTabsSection(content);
        if (chartTabsSection is null) return false;

        return Regex.IsMatch(chartTabsSection, @"MudTabPanel\s+Text=""Trades""");
    }

    /// <summary>
    /// The chart tabs section SHALL contain a "P&amp;L" tab panel.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_PnLTabPresent(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartTabsSection = ExtractLastMudTabsSection(content);
        if (chartTabsSection is null) return false;

        // P&L is encoded as P&amp;L in Razor markup
        return chartTabsSection.Contains(@"Text=""P&amp;L""") || chartTabsSection.Contains(@"Text=""P&L""");
    }

    /// <summary>
    /// The chart tabs section SHALL contain a "Config" tab panel.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_ConfigTabPresent(PositiveInt _)
    {
        var content = ReadResultDetailRazor();
        var chartTabsSection = ExtractLastMudTabsSection(content);
        if (chartTabsSection is null) return false;

        return Regex.IsMatch(chartTabsSection, @"MudTabPanel\s+Text=""Config""");
    }

    // ========================================================================
    // Preservation 3 — Standalone equity curve above metrics remains
    // ========================================================================

    /// <summary>
    /// ResultDetail.razor SHALL render an EquityCurveChart above the metrics section
    /// (outside the tab panels), with ShowDrawdown="true".
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool StandaloneEquityCurve_AboveMetrics_Exists(PositiveInt _)
    {
        var content = ReadResultDetailRazor();

        // The standalone equity curve is rendered before the first MudGrid (metrics section)
        // and before the last MudTabs (chart tabs section)
        var firstMudGrid = content.IndexOf("<MudGrid", StringComparison.Ordinal);
        var lastMudTabs = content.LastIndexOf("<MudTabs", StringComparison.Ordinal);

        if (firstMudGrid < 0 || lastMudTabs < 0) return false;

        // Find EquityCurveChart with ShowDrawdown="true" that appears before the metrics grid
        var standaloneSection = content[..firstMudGrid];
        var hasStandaloneEquityCurve = standaloneSection.Contains("EquityCurveChart")
            && standaloneSection.Contains(@"ShowDrawdown=""true""");

        return hasStandaloneEquityCurve;
    }

    // ========================================================================
    // Preservation 4 — History.razor search/filter/sort/delete functionality
    // ========================================================================

    /// <summary>
    /// History.razor SHALL contain a FilterFunc method for search/filter functionality.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasFilterFunc(PositiveInt _)
    {
        var content = ReadHistoryRazor();
        return content.Contains("FilterFunc");
    }

    /// <summary>
    /// History.razor SHALL contain a MudTable with sortable columns.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasSortableMudTable(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        var hasMudTable = content.Contains("<MudTable");
        var hasSortLabel = content.Contains("MudTableSortLabel");

        return hasMudTable && hasSortLabel;
    }

    /// <summary>
    /// History.razor SHALL contain a search text field for filtering results.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasSearchTextField(PositiveInt _)
    {
        var content = ReadHistoryRazor();
        return content.Contains("_searchString") && content.Contains("Placeholder=\"Search...\"");
    }

    /// <summary>
    /// History.razor SHALL contain DeleteRun and DeleteAllRuns methods.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasDeleteMethods(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        var hasDeleteRun = Regex.IsMatch(content, @"Task\s+DeleteRun\s*\(");
        var hasDeleteAllRuns = Regex.IsMatch(content, @"Task\s+DeleteAllRuns\s*\(");

        return hasDeleteRun && hasDeleteAllRuns;
    }

    // ========================================================================
    // Preservation 5 — DeleteRun and DeleteAllRuns call ResultRepo.DeleteAsync
    // ========================================================================

    /// <summary>
    /// History.razor DeleteRun method SHALL call ResultRepo.DeleteAsync.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_DeleteRun_CallsDeleteAsync(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        // Extract the DeleteRun method body
        var deleteRunMatch = Regex.Match(content, @"Task\s+DeleteRun\s*\([^)]*\)\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!deleteRunMatch.Success) return false;

        var methodBody = deleteRunMatch.Groups[1].Value;
        return methodBody.Contains("ResultRepo.DeleteAsync");
    }

    /// <summary>
    /// History.razor DeleteAllRuns method SHALL call ResultRepo.DeleteAsync.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_DeleteAllRuns_CallsDeleteAsync(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        // Extract the DeleteAllRuns method body
        var deleteAllMatch = Regex.Match(content, @"Task\s+DeleteAllRuns\s*\([^)]*\)\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!deleteAllMatch.Success) return false;

        var methodBody = deleteAllMatch.Groups[1].Value;
        return methodBody.Contains("ResultRepo.DeleteAsync");
    }

    // ========================================================================
    // Helper methods
    // ========================================================================

    /// <summary>
    /// Extracts the content of the "Charts" MudTabPanel from the last MudTabs section.
    /// </summary>
    private static string? ExtractChartsTabPanel(string content)
    {
        var tabsSection = ExtractLastMudTabsSection(content);
        if (tabsSection is null) return null;

        // Find the Charts tab panel
        var chartsStart = tabsSection.IndexOf(@"Text=""Charts""", StringComparison.Ordinal);
        if (chartsStart < 0) return null;

        // Find the start of this MudTabPanel tag
        var panelStart = tabsSection.LastIndexOf("<MudTabPanel", chartsStart, StringComparison.Ordinal);
        if (panelStart < 0) return null;

        // Find the closing </MudTabPanel> for this panel
        // We need to find the next </MudTabPanel> or next <MudTabPanel after chartsStart
        var nextPanelStart = tabsSection.IndexOf("<MudTabPanel", chartsStart + 1, StringComparison.Ordinal);
        var panelEnd = tabsSection.IndexOf("</MudTabPanel>", chartsStart, StringComparison.Ordinal);

        if (panelEnd < 0) return null;

        // Use the earlier of next panel start or panel end as the boundary
        int endBoundary;
        if (nextPanelStart >= 0 && nextPanelStart < panelEnd)
            endBoundary = nextPanelStart;
        else
            endBoundary = panelEnd + "</MudTabPanel>".Length;

        return tabsSection[panelStart..endBoundary];
    }

    /// <summary>
    /// Extracts the last MudTabs section (the chart tabs) from the content.
    /// </summary>
    private static string? ExtractLastMudTabsSection(string content)
    {
        var mudTabsStarts = new List<int>();
        var idx = 0;
        while ((idx = content.IndexOf("<MudTabs", idx, StringComparison.Ordinal)) >= 0)
        {
            mudTabsStarts.Add(idx);
            idx++;
        }

        if (mudTabsStarts.Count < 2) return null;

        var chartTabsStart = mudTabsStarts[^1]; // Last MudTabs block
        var chartTabsEnd = content.IndexOf("</MudTabs>", chartTabsStart, StringComparison.Ordinal);
        if (chartTabsEnd < 0) return null;

        return content[chartTabsStart..(chartTabsEnd + "</MudTabs>".Length)];
    }
}
