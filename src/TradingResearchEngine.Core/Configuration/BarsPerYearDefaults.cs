namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Canonical intraday bar-count constants for Forex (24h × 252 trading days).
/// Used by PreflightValidator and Step2DataExecutionWindow for auto-population.
/// </summary>
public static class BarsPerYearDefaults
{
    /// <summary>1-minute bars per year: 252 × 1440.</summary>
    public const int M1 = 362880;

    /// <summary>5-minute bars per year: 252 × 288.</summary>
    public const int M5 = 72576;

    /// <summary>15-minute bars per year: 252 × 96.</summary>
    public const int M15 = 24192;

    /// <summary>30-minute bars per year: 252 × 48.</summary>
    public const int M30 = 12096;

    /// <summary>1-hour bars per year: 252 × 24.</summary>
    public const int H1 = 6048;

    /// <summary>2-hour bars per year: 252 × 12.</summary>
    public const int H2 = 3024;

    /// <summary>4-hour bars per year: 252 × 6.</summary>
    public const int H4 = 1512;

    /// <summary>Daily bars per year: 252 × 1.</summary>
    public const int D1 = 252;

    private static readonly Dictionary<string, int> _lookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M1"] = M1, ["1m"] = M1,
        ["M5"] = M5, ["5m"] = M5,
        ["M15"] = M15, ["15m"] = M15,
        ["M30"] = M30, ["30m"] = M30,
        ["H1"] = H1, ["1H"] = H1,
        ["H2"] = H2, ["2H"] = H2,
        ["H4"] = H4, ["4H"] = H4,
        ["D1"] = D1, ["Daily"] = D1, ["1D"] = D1,
    };

    private static readonly Dictionary<string, (int barsPerDay, string label)> _timeframeInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M1"] = (1440, "1-minute"),
        ["M5"] = (288, "5-minute"),
        ["M15"] = (96, "15-minute"),
        ["M30"] = (48, "30-minute"),
        ["H1"] = (24, "1-hour"),
        ["H2"] = (12, "2-hour"),
        ["H4"] = (6, "4-hour"),
        ["D1"] = (1, "daily"),
        ["Daily"] = (1, "daily"),
    };

    /// <summary>Returns the BarsPerYear for a given timeframe string, or null if unknown.</summary>
    public static int? ForTimeframe(string timeframe)
        => _lookup.TryGetValue(timeframe, out var value) ? value : null;

    /// <summary>
    /// Converts a bar count to a human-readable duration string.
    /// E.g. 500 bars at M15 → "~52 trading days of 15-minute data required"
    /// </summary>
    public static string BarsToHumanDuration(int bars, string timeframe)
    {
        var bpy = ForTimeframe(timeframe);
        if (bpy is null)
            return $"{bars} bars";

        int barsPerDay = bpy.Value / 252;
        if (barsPerDay <= 0)
            return $"{bars} bars";

        int tradingDays = (int)Math.Ceiling((double)bars / barsPerDay);

        var label = _timeframeInfo.TryGetValue(timeframe, out var info) ? info.label : timeframe;

        return $"~{tradingDays} trading days of {label} data required";
    }
}
