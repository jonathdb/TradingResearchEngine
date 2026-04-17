using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Helpers;

/// <summary>
/// Pure static computation helpers for chart data transformations.
/// These are extracted into the Application layer so UnitTests can reference them directly.
/// </summary>
public static class ChartComputationHelpers
{
    /// <summary>
    /// Computes monthly returns as percentages from an equity curve, grouped by calendar month.
    /// Returns one entry per calendar month with the percentage change from first to last equity point.
    /// </summary>
    public static IReadOnlyList<MonthlyReturn> ComputeMonthlyReturns(IReadOnlyList<EquityCurvePoint> curve)
    {
        if (curve is null || curve.Count == 0)
            return Array.Empty<MonthlyReturn>();

        var grouped = curve
            .GroupBy(p => new { p.Timestamp.Year, p.Timestamp.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month);

        var results = new List<MonthlyReturn>();
        foreach (var group in grouped)
        {
            var points = group.OrderBy(p => p.Timestamp).ToList();
            var first = points[0].TotalEquity;
            var last = points[^1].TotalEquity;
            var returnPct = first != 0m ? (last - first) / first * 100m : 0m;
            results.Add(new MonthlyReturn(group.Key.Year, group.Key.Month, returnPct));
        }

        return results;
    }

    /// <summary>
    /// Bins trade PnL values into the specified number of buckets covering the full PnL range.
    /// Returns empty collection for empty input.
    /// </summary>
    public static IReadOnlyList<HistogramBin> BinTradePnl(IReadOnlyList<ClosedTrade> trades, int bins = 20)
    {
        if (trades is null || trades.Count == 0 || bins <= 0)
            return Array.Empty<HistogramBin>();

        var pnlValues = trades.Select(t => t.NetPnl).ToList();
        return BinValues(pnlValues, bins);
    }

    /// <summary>
    /// Bins trade holding periods (duration in bars approximated from time difference) into a histogram.
    /// </summary>
    public static IReadOnlyList<HistogramBin> BinHoldingPeriods(IReadOnlyList<ClosedTrade> trades, int bins = 20)
    {
        if (trades is null || trades.Count == 0 || bins <= 0)
            return Array.Empty<HistogramBin>();

        var durations = trades
            .Select(t => (decimal)(t.ExitTime - t.EntryTime).TotalHours)
            .ToList();

        return BinValues(durations, bins);
    }

    /// <summary>
    /// Generic binning of decimal values into a fixed number of equal-width bins.
    /// </summary>
    internal static IReadOnlyList<HistogramBin> BinValues(IReadOnlyList<decimal> values, int bins)
    {
        if (values.Count == 0 || bins <= 0)
            return Array.Empty<HistogramBin>();

        var min = values.Min();
        var max = values.Max();

        // Handle single-value case
        if (min == max)
        {
            var result = new HistogramBin[bins];
            for (int i = 0; i < bins; i++)
                result[i] = new HistogramBin(min, max, i == bins / 2 ? values.Count : 0);
            return result;
        }

        var binWidth = (max - min) / bins;
        var histogram = new int[bins];

        foreach (var v in values)
        {
            var idx = (int)((v - min) / binWidth);
            if (idx >= bins) idx = bins - 1; // clamp max value into last bin
            histogram[idx]++;
        }

        var output = new HistogramBin[bins];
        for (int i = 0; i < bins; i++)
        {
            var lo = min + i * binWidth;
            var hi = min + (i + 1) * binWidth;
            output[i] = new HistogramBin(lo, hi, histogram[i]);
        }

        return output;
    }
}

/// <summary>A single monthly return entry.</summary>
public sealed record MonthlyReturn(int Year, int Month, decimal ReturnPercent);

/// <summary>A single histogram bin with lower/upper bounds and count.</summary>
public sealed record HistogramBin(decimal LowerBound, decimal UpperBound, int Count);
