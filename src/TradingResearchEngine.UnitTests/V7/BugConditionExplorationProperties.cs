using FsCheck;
using FsCheck.Xunit;
using System.Text.RegularExpressions;

namespace TradingResearchEngine.UnitTests.V7;

/// <summary>
/// Feature: v7-bugfix-pass, Property 1: Bug Condition
/// Exploration tests that parse the actual Razor source files to verify
/// the expected (fixed) behavior. These tests MUST FAIL on unfixed code,
/// confirming the bugs exist.
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
/// </summary>
public class BugConditionExplorationProperties
{
    /// <summary>
    /// Resolves the path to the source file relative to the test execution directory.
    /// Walks up from bin/Debug/net8.0 to the repo root, then navigates to the source file.
    /// </summary>
    private static string GetSourceFilePath(string relativePath)
    {
        // Start from the test assembly location and walk up to find the repo root
        var dir = AppContext.BaseDirectory;
        // Walk up until we find the src folder or hit the root
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
    // Bug 1 — Duplicate Chart Tabs in ResultDetail.razor
    // ========================================================================

    /// <summary>
    /// Within the MudTabs chart tab section, EquityCurveChart appears exactly once.
    /// On unfixed code, it appears multiple times (standalone "Equity" tab + "Charts" tab).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_EquityCurveChart_AppearsExactlyOnce_InTabSection(PositiveInt _)
    {
        var content = ReadResultDetailRazor();

        // Extract the chart tabs section (the second MudTabs block — the one with chart panels)
        var chartTabsContent = ExtractChartTabsSection(content);
        if (chartTabsContent is null) return false;

        // Count EquityCurveChart occurrences within the chart tabs section
        var count = Regex.Matches(chartTabsContent, @"EquityCurveChart").Count;

        return count == 1;
    }

    /// <summary>
    /// Within the MudTabs chart tab section, DrawdownChart appears exactly once.
    /// On unfixed code, it appears in a standalone "Drawdown" tab.
    /// After fix, it should appear exactly once in the "Charts" tab.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_DrawdownChart_AppearsExactlyOnce_InTabSection(PositiveInt _)
    {
        var content = ReadResultDetailRazor();

        // Extract the chart tabs section
        var chartTabsContent = ExtractChartTabsSection(content);
        if (chartTabsContent is null) return false;

        // Count DrawdownChart occurrences within the chart tabs section
        var count = Regex.Matches(chartTabsContent, @"DrawdownChart").Count;

        return count == 1;
    }

    /// <summary>
    /// No tab named "Equity" exists as a standalone tab in the chart tabs.
    /// On unfixed code, there is a MudTabPanel Text="Equity" that duplicates content.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_NoStandaloneEquityTab_Exists(PositiveInt _)
    {
        var content = ReadResultDetailRazor();

        // Extract the chart tabs section
        var chartTabsContent = ExtractChartTabsSection(content);
        if (chartTabsContent is null) return false;

        // Check that no tab panel named "Equity" exists
        var hasEquityTab = Regex.IsMatch(chartTabsContent, @"MudTabPanel\s+Text=""Equity""");

        return !hasEquityTab;
    }

    /// <summary>
    /// No tab named "Drawdown" exists as a standalone tab in the chart tabs.
    /// On unfixed code, there is a MudTabPanel Text="Drawdown" that duplicates content.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ChartTabs_NoStandaloneDrawdownTab_Exists(PositiveInt _)
    {
        var content = ReadResultDetailRazor();

        // Extract the chart tabs section
        var chartTabsContent = ExtractChartTabsSection(content);
        if (chartTabsContent is null) return false;

        // Check that no tab panel named "Drawdown" exists
        var hasDrawdownTab = Regex.IsMatch(chartTabsContent, @"MudTabPanel\s+Text=""Drawdown""");

        return !hasDrawdownTab;
    }

    // ========================================================================
    // Bug 2 — Missing CancellationToken in History.razor
    // ========================================================================

    /// <summary>
    /// History.razor contains @implements IDisposable.
    /// On unfixed code, this directive is missing.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_ImplementsIDisposable(PositiveInt _)
    {
        var content = ReadHistoryRazor();
        return content.Contains("@implements IDisposable");
    }

    /// <summary>
    /// History.razor contains a CancellationTokenSource field declaration.
    /// On unfixed code, this field is missing.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasCancellationTokenSourceField(PositiveInt _)
    {
        var content = ReadHistoryRazor();
        return content.Contains("CancellationTokenSource");
    }

    /// <summary>
    /// All ListAsync calls in History.razor pass a cancellation token argument.
    /// On unfixed code, ListAsync is called without a token.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_AllListAsyncCallsPassCancellationToken(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        // Find all ListAsync calls
        var listAsyncCalls = Regex.Matches(content, @"ListAsync\s*\(([^)]*)\)");
        if (listAsyncCalls.Count == 0) return false;

        // Every ListAsync call must have a non-empty argument (the token)
        foreach (Match match in listAsyncCalls)
        {
            var args = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(args)) return false;
        }

        return true;
    }

    /// <summary>
    /// All DeleteAsync calls in History.razor pass a cancellation token argument.
    /// On unfixed code, DeleteAsync is called with only the id parameter.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_AllDeleteAsyncCallsPassCancellationToken(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        // Find all DeleteAsync calls
        var deleteAsyncCalls = Regex.Matches(content, @"DeleteAsync\s*\(([^)]*)\)");
        if (deleteAsyncCalls.Count == 0) return false;

        // Every DeleteAsync call must have at least two arguments (id + token)
        foreach (Match match in deleteAsyncCalls)
        {
            var args = match.Groups[1].Value.Trim();
            // Must contain a comma indicating multiple arguments (id, token)
            if (!args.Contains(',')) return false;
        }

        return true;
    }

    /// <summary>
    /// History.razor contains a Dispose() method that cancels and disposes the token source.
    /// On unfixed code, there is no Dispose method.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasDisposeMethod(PositiveInt _)
    {
        var content = ReadHistoryRazor();

        // Check for Dispose method presence
        var hasDispose = Regex.IsMatch(content, @"void\s+Dispose\s*\(\s*\)");
        if (!hasDispose) return false;

        // Check that it cancels the token source
        var hasCancel = content.Contains(".Cancel()");
        var hasDisposeCall = content.Contains(".Dispose()");

        return hasCancel && hasDisposeCall;
    }

    /// <summary>
    /// History.razor contains catch (OperationCanceledException) handling.
    /// On unfixed code, there is no such exception handling.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool History_HasOperationCanceledExceptionHandling(PositiveInt _)
    {
        var content = ReadHistoryRazor();
        return content.Contains("OperationCanceledException");
    }

    // ========================================================================
    // Helper methods
    // ========================================================================

    /// <summary>
    /// Extracts the chart tabs MudTabs section from ResultDetail.razor content.
    /// The chart tabs section is the second MudTabs block (the first is the metrics tabs).
    /// It contains tabs like "Equity", "Drawdown", "Charts", "Trades", "P&amp;L", "Config".
    /// </summary>
    private static string? ExtractChartTabsSection(string content)
    {
        // Find all MudTabs blocks — the chart tabs is the second one
        // We look for <MudTabs and find the matching closing </MudTabs>
        var mudTabsStarts = new List<int>();
        var idx = 0;
        while ((idx = content.IndexOf("<MudTabs", idx, StringComparison.Ordinal)) >= 0)
        {
            mudTabsStarts.Add(idx);
            idx++;
        }

        // The chart tabs section is the last MudTabs block (second one)
        if (mudTabsStarts.Count < 2) return null;

        var chartTabsStart = mudTabsStarts[^1]; // Last MudTabs block
        var chartTabsEnd = content.IndexOf("</MudTabs>", chartTabsStart, StringComparison.Ordinal);
        if (chartTabsEnd < 0) return null;

        return content[chartTabsStart..(chartTabsEnd + "</MudTabs>".Length)];
    }
}
