using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Web.Components.Builder;

/// <summary>
/// Static class providing timeframe options for the strategy builder UI.
/// Drives the timeframe selector in Step2DataExecutionWindow.
/// </summary>
public static class TimeframeOptions
{
    /// <summary>All available timeframe options with display labels and BarsPerYear values.</summary>
    public static readonly IReadOnlyList<TimeframeOption> All = new[]
    {
        new TimeframeOption("M1", "1m", BarsPerYearDefaults.M1),
        new TimeframeOption("M5", "5m", BarsPerYearDefaults.M5),
        new TimeframeOption("M15", "15m", BarsPerYearDefaults.M15),
        new TimeframeOption("M30", "30m", BarsPerYearDefaults.M30),
        new TimeframeOption("H1", "1H", BarsPerYearDefaults.H1),
        new TimeframeOption("H2", "2H", BarsPerYearDefaults.H2),
        new TimeframeOption("H4", "4H", BarsPerYearDefaults.H4),
        new TimeframeOption("D1", "Daily", BarsPerYearDefaults.D1),
    };
}

/// <summary>Represents a selectable timeframe option in the builder UI.</summary>
/// <param name="Value">The timeframe key used in config (e.g. "M15", "H4", "D1").</param>
/// <param name="Label">Human-readable display label (e.g. "15m", "4H", "Daily").</param>
/// <param name="BarsPerYear">Canonical bars per year for this timeframe.</param>
public sealed record TimeframeOption(string Value, string Label, int BarsPerYear);
