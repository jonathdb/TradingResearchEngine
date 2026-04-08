using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Sessions;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Classifies trades into regime buckets (volatility, trend, session) and
/// computes per-regime performance metrics.
/// </summary>
public sealed class RegimeSegmentationWorkflow
{
    private readonly ISessionCalendar? _sessionCalendar;

    public RegimeSegmentationWorkflow(ISessionCalendar? sessionCalendar = null)
        => _sessionCalendar = sessionCalendar;

    /// <summary>Segments a backtest result by regime dimensions.</summary>
    public RegimePerformanceReport Analyse(
        BacktestResult result,
        IReadOnlyList<Core.DataHandling.BarRecord> bars,
        RegimeSegmentationOptions options)
    {
        var segments = new List<RegimeSegment>();

        // Volatility regimes
        var volRegimes = ClassifyVolatilityRegimes(bars, options);
        segments.AddRange(BuildSegments(result.Trades, volRegimes, "Volatility"));

        // Trend regimes
        var trendRegimes = ClassifyTrendRegimes(bars, options);
        segments.AddRange(BuildSegments(result.Trades, trendRegimes, "Trend"));

        // Session regimes
        if (_sessionCalendar is not null)
        {
            var sessionRegimes = bars.ToDictionary(
                b => b.Timestamp,
                b => _sessionCalendar.ClassifySession(b.Timestamp));
            segments.AddRange(BuildSegments(result.Trades, sessionRegimes, "Session"));
        }

        return new RegimePerformanceReport(segments);
    }

    private static Dictionary<DateTimeOffset, string> ClassifyVolatilityRegimes(
        IReadOnlyList<Core.DataHandling.BarRecord> bars, RegimeSegmentationOptions options)
    {
        var regimes = new Dictionary<DateTimeOffset, string>();
        var returns = new List<decimal>();

        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i - 1].Close > 0m)
                returns.Add((bars[i].Close - bars[i - 1].Close) / bars[i - 1].Close);
            else
                returns.Add(0m);

            if (returns.Count < options.VolatilityLookback)
            {
                regimes[bars[i].Timestamp] = "Medium";
                continue;
            }

            var window = returns.Skip(returns.Count - options.VolatilityLookback).ToList();
            decimal mean = window.Average();
            decimal variance = window.Sum(r => (r - mean) * (r - mean)) / window.Count;
            decimal vol = (decimal)Math.Sqrt((double)variance);

            // Simple threshold-based classification
            regimes[bars[i].Timestamp] = vol < options.LowVolThreshold * 0.01m ? "Low"
                : vol > options.HighVolThreshold * 0.01m ? "High"
                : "Medium";
        }
        return regimes;
    }

    private static Dictionary<DateTimeOffset, string> ClassifyTrendRegimes(
        IReadOnlyList<Core.DataHandling.BarRecord> bars, RegimeSegmentationOptions options)
    {
        var regimes = new Dictionary<DateTimeOffset, string>();
        for (int i = 0; i < bars.Count; i++)
        {
            if (i < options.TrendLookback)
            {
                regimes[bars[i].Timestamp] = "Neutral";
                continue;
            }

            decimal smaStart = bars.Skip(i - options.TrendLookback).Take(options.TrendLookback / 2).Average(b => b.Close);
            decimal smaEnd = bars.Skip(i - options.TrendLookback / 2).Take(options.TrendLookback / 2).Average(b => b.Close);
            decimal slope = smaEnd - smaStart;

            regimes[bars[i].Timestamp] = slope > 0 ? "Uptrend" : slope < 0 ? "Downtrend" : "Neutral";
        }
        return regimes;
    }

    private static List<RegimeSegment> BuildSegments(
        IReadOnlyList<ClosedTrade> trades,
        Dictionary<DateTimeOffset, string> regimeMap,
        string dimension)
    {
        var grouped = trades
            .GroupBy(t => FindClosestRegime(t.EntryTime, regimeMap))
            .Where(g => g.Key is not null);

        var segments = new List<RegimeSegment>();
        foreach (var group in grouped)
        {
            var list = group.ToList();
            int wins = list.Count(t => t.NetPnl > 0);
            decimal winRate = list.Count > 0 ? (decimal)wins / list.Count : 0m;
            decimal expectancy = list.Count > 0 ? list.Average(t => t.NetPnl) : 0m;
            double avgHoldTicks = list.Count > 0 ? list.Average(t => (t.ExitTime - t.EntryTime).Ticks) : 0;
            decimal ddContrib = list.Count > 0 ? list.Where(t => t.NetPnl < 0).Sum(t => Math.Abs(t.NetPnl)) : 0m;

            segments.Add(new RegimeSegment(
                group.Key!,
                dimension,
                list.Count,
                winRate,
                expectancy,
                TimeSpan.FromTicks((long)avgHoldTicks),
                ddContrib));
        }
        return segments;
    }

    private static string? FindClosestRegime(DateTimeOffset timestamp, Dictionary<DateTimeOffset, string> regimeMap)
    {
        if (regimeMap.TryGetValue(timestamp, out var regime)) return regime;
        // Find closest timestamp
        var closest = regimeMap.Keys
            .Where(k => k <= timestamp)
            .OrderByDescending(k => k)
            .FirstOrDefault();
        return closest != default ? regimeMap[closest] : regimeMap.Values.FirstOrDefault();
    }
}
