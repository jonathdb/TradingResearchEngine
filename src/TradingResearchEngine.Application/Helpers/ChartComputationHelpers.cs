using TradingResearchEngine.Application.Research;
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
    public static IReadOnlyList<HistogramBin> BinValues(IReadOnlyList<decimal> values, int bins)
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

    /// <summary>
    /// Computes tight Y-axis bounds with ±1% padding around the data range.
    /// </summary>
    /// <param name="minEquity">The minimum equity value in the curve.</param>
    /// <param name="maxEquity">The maximum equity value in the curve.</param>
    /// <returns>A tuple of (Lower, Upper) bounds for the Y-axis.</returns>
    public static (decimal Lower, decimal Upper) ComputeYAxisRange(decimal minEquity, decimal maxEquity)
    {
        var lower = minEquity * 0.99m;
        var upper = maxEquity * 1.01m;
        return (lower, upper);
    }

    /// <summary>
    /// Generates formatted annotation strings for all monthly return cells.
    /// Each annotation displays the return percentage to one decimal place.
    /// </summary>
    /// <param name="returns">The collection of monthly return values.</param>
    /// <returns>A list of heatmap annotations with formatted text.</returns>
    public static IReadOnlyList<HeatmapAnnotation> ComputeHeatmapAnnotations(IReadOnlyList<MonthlyReturn> returns)
    {
        if (returns is null || returns.Count == 0)
            return Array.Empty<HeatmapAnnotation>();

        return returns
            .Select(r => new HeatmapAnnotation(r.Year, r.Month, $"{r.ReturnPercent:F1}%"))
            .ToList();
    }

    /// <summary>
    /// Computes the dynamic chart height for the heatmap based on the number of years displayed.
    /// Ensures at least 30 pixels per year row for readability.
    /// </summary>
    /// <param name="yearCount">The number of distinct calendar years in the data.</param>
    /// <returns>The minimum chart height in pixels.</returns>
    public static int ComputeHeatmapHeight(int yearCount)
    {
        return yearCount * 30;
    }

    /// <summary>
    /// Computes the progress percentage for the research checklist based on completed steps.
    /// </summary>
    /// <param name="steps">The collection of research step statuses.</param>
    /// <returns>The percentage of completed steps (0–100).</returns>
    public static double ComputeProgressPercent(IReadOnlyList<ResearchStepStatus> steps)
    {
        if (steps is null || steps.Count == 0)
            return 0.0;

        var completedCount = steps.Count(s => s == ResearchStepStatus.Completed);
        return (double)completedCount / steps.Count * 100.0;
    }

    /// <summary>
    /// Computes the progress text for the research checklist showing completed count.
    /// </summary>
    /// <param name="steps">The collection of research step statuses.</param>
    /// <returns>A string in the format "{X} of 9 completed".</returns>
    public static string ComputeProgressText(IReadOnlyList<ResearchStepStatus> steps)
    {
        if (steps is null || steps.Count == 0)
            return "0 of 9 completed";

        var completedCount = steps.Count(s => s == ResearchStepStatus.Completed);
        return $"{completedCount} of 9 completed";
    }

    /// <summary>
    /// Descriptions for all 9 research checklist steps, keyed by step identifier.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StepDescriptions = new Dictionary<string, string>
    {
        ["InitialBacktest"] = "Runs the strategy on historical data to establish baseline performance metrics.",
        ["MonteCarloRobustness"] = "Resamples trade sequences to assess whether results are robust to ordering effects.",
        ["WalkForwardValidation"] = "Tests the strategy on sequential out-of-sample windows to detect overfitting.",
        ["RegimeSensitivity"] = "Evaluates performance across different market regimes (trending, ranging, volatile).",
        ["RealismImpact"] = "Measures how execution costs (slippage, commissions) degrade theoretical performance.",
        ["ParameterSurface"] = "Maps strategy performance across parameter variations to identify fragile optima.",
        ["FinalHeldOutTest"] = "Runs the strategy on a sealed test set never used during development.",
        ["PropFirmEvaluation"] = "Evaluates whether the strategy meets prop firm challenge rules and economics.",
        ["CpcvDone"] = "Applies combinatorial purged cross-validation to quantify probability of overfitting."
    };
}

/// <summary>A single monthly return entry.</summary>
public sealed record MonthlyReturn(int Year, int Month, decimal ReturnPercent);

/// <summary>A single histogram bin with lower/upper bounds and count.</summary>
public sealed record HistogramBin(decimal LowerBound, decimal UpperBound, int Count);

/// <summary>A text annotation for a heatmap cell.</summary>
public sealed record HeatmapAnnotation(int Year, int Month, string Text);
