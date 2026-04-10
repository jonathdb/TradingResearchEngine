using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Downloads historical candle data from Dukascopy's free datafeed.
/// No API key required. Data is LZMA-compressed binary (.bi5 format).
/// Supports forex, gold, indices, and CFDs.
/// </summary>
public sealed class DukascopyDataProvider : IDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DukascopyDataProvider> _logger;

    public DukascopyDataProvider(HttpClient httpClient, ILogger<DukascopyDataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Check cache first
        var cachePath = GetCachePath(symbol, interval, from, to);
        if (File.Exists(cachePath))
        {
            _logger.LogInformation("Dukascopy: loading from cache {Path}", cachePath);
            var cached = DukascopyHelpers.LoadFromCsv(cachePath, symbol, interval);
            foreach (var bar in cached)
            {
                if (bar.Timestamp >= from && bar.Timestamp <= to)
                    yield return bar;
            }
            yield break;
        }

        var pointSize = DukascopyHelpers.PointSizes.TryGetValue(symbol, out var ps) ? ps : 100_000m;

        var dates = DukascopyHelpers.BuildTradingDays(from.Date, to.Date);

        _logger.LogInformation("Dukascopy: fetching {Symbol} — {Count} trading days ({From:yyyy-MM-dd} to {To:yyyy-MM-dd})",
            symbol, dates.Count, from, to);

        // Parallel download (up to 4 concurrent requests)
        var allMinuteBars = new List<BarRecord>();
        var batches = dates.Chunk(4);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = batch.Select(d => FetchDayAsync(symbol, d, pointSize, ct));
            var results = await Task.WhenAll(tasks);
            foreach (var dayBars in results)
                allMinuteBars.AddRange(dayBars);
        }

        // Sort by timestamp (parallel downloads may arrive out of order)
        allMinuteBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("Dukascopy: downloaded {Count} minute bars, aggregating to {Interval}",
            allMinuteBars.Count, interval);

        var aggregated = DukascopyHelpers.Aggregate(allMinuteBars, interval, symbol);

        // Save to cache
        DukascopyHelpers.SaveToCsv(cachePath, aggregated);
        _logger.LogInformation("Dukascopy: cached {Count} bars to {Path}", aggregated.Count, cachePath);

        foreach (var bar in aggregated)
        {
            if (bar.Timestamp >= from && bar.Timestamp <= to)
                yield return bar;
        }
    }

    public IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Dukascopy tick data not yet implemented. Use bar data.");
        return EmptyAsyncEnumerable<TickRecord>.Instance;
    }

    private async Task<List<BarRecord>> FetchDayAsync(
        string symbol, DateTime date, decimal pointSize, CancellationToken ct)
    {
        var url = DukascopyHelpers.BuildDayUrl(symbol, date);

        byte[] compressed;
        try { compressed = await _httpClient.GetByteArrayAsync(url, ct); }
        catch (HttpRequestException)
        {
            _logger.LogDebug("No data for {Symbol} on {Date:yyyy-MM-dd}", symbol, date);
            return new();
        }

        if (compressed.Length < 13) return new();

        byte[] decompressed;
        try { decompressed = DukascopyHelpers.Decompress(compressed); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Decompress failed for {Symbol} {Date:yyyy-MM-dd}", symbol, date);
            return new();
        }

        return DukascopyHelpers.ParseCandles(decompressed, date, symbol, pointSize);
    }

    // --- CSV Cache ---

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TradingResearchEngine", "DukascopyCache");

    private static string GetCachePath(string symbol, string interval, DateTimeOffset from, DateTimeOffset to)
    {
        var name = $"{symbol}_{interval}_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
        return Path.Combine(CacheDir, name);
    }
}
