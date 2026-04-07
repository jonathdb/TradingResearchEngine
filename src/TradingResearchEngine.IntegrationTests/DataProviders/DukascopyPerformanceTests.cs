using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

/// <summary>
/// Performance profiling tests for the Dukascopy pipeline.
/// Measures time spent in each phase: download, decompress, parse, aggregate, strategy.
/// </summary>
public class DukascopyPerformanceTests
{
    private readonly ILogger<DukascopyDataProvider> _logger =
        NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

    [Fact]
    public async Task Profile_SingleDay_Download_Decompress_Parse()
    {
        var http = new HttpClient();
        var provider = new DukascopyDataProvider(http, _logger);

        var sw = Stopwatch.StartNew();

        // Phase 1: Download + decompress + parse (all inside GetBars)
        var bars = new List<BarRecord>();
        var from = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 3, 1, 23, 59, 59, TimeSpan.Zero);

        await foreach (var bar in provider.GetBars("EURUSD", "1m", from, to))
            bars.Add(bar);

        var downloadTime = sw.ElapsedMilliseconds;
        Assert.True(bars.Count > 0, "No bars returned");

        // Output timing
        Console.WriteLine($"[PERF] 1 day, 1m bars: {bars.Count} bars in {downloadTime}ms");
        Console.WriteLine($"[PERF] Per bar: {(double)downloadTime / bars.Count:F2}ms");
    }

    [Fact]
    public async Task Profile_FiveDays_Daily_Aggregation()
    {
        var http = new HttpClient();
        var provider = new DukascopyDataProvider(http, _logger);

        var sw = Stopwatch.StartNew();

        var bars = new List<BarRecord>();
        var from = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 3, 5, 23, 59, 59, TimeSpan.Zero);

        await foreach (var bar in provider.GetBars("EURUSD", "1d", from, to))
            bars.Add(bar);

        var totalTime = sw.ElapsedMilliseconds;

        Console.WriteLine($"[PERF] 5 days, 1D bars: {bars.Count} bars in {totalTime}ms");
        Console.WriteLine($"[PERF] ~{totalTime / 5}ms per day download+decompress");
    }

    [Fact]
    public async Task Profile_Strategy_Processing_Speed()
    {
        // Generate synthetic bars to isolate strategy speed from download speed
        var bars = Enumerable.Range(0, 10_000)
            .Select(i => new BarRecord("TEST", "1m",
                1.1000m + i * 0.0001m,
                1.1005m + i * 0.0001m,
                1.0995m + i * 0.0001m,
                1.1002m + i * 0.0001m,
                100m,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i)))
            .ToList();

        // Simulate SMA crossover logic (the slow part)
        var closes = new List<decimal>();
        int signals = 0;
        var sw = Stopwatch.StartNew();

        foreach (var bar in bars)
        {
            closes.Add(bar.Close);
            if (closes.Count < 30) continue;

            // This is the O(n) per bar operation that causes O(n²) total
            decimal fastSma = closes.Skip(closes.Count - 10).Take(10).Average();
            decimal slowSma = closes.Skip(closes.Count - 30).Take(30).Average();

            if (fastSma > slowSma) signals++;
        }

        var strategyTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"[PERF] Strategy on {bars.Count} bars: {strategyTime}ms, {signals} signals");
        Console.WriteLine($"[PERF] Per bar: {(double)strategyTime / bars.Count:F4}ms");

        // Now test with O(1) rolling average
        closes.Clear();
        signals = 0;
        decimal fastSum = 0, slowSum = 0;
        sw.Restart();

        foreach (var bar in bars)
        {
            closes.Add(bar.Close);
            int n = closes.Count;

            if (n <= 10) fastSum += bar.Close;
            else { fastSum += bar.Close - closes[n - 11]; }

            if (n <= 30) slowSum += bar.Close;
            else { slowSum += bar.Close - closes[n - 31]; }

            if (n < 30) continue;

            decimal fast = fastSum / 10m;
            decimal slow = slowSum / 30m;
            if (fast > slow) signals++;
        }

        var optimizedTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"[PERF] Optimized on {bars.Count} bars: {optimizedTime}ms, {signals} signals");
        Console.WriteLine($"[PERF] Speedup: {(double)strategyTime / Math.Max(1, optimizedTime):F1}x");
    }
}
