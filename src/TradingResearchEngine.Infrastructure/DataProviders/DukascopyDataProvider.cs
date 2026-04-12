using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>Selects which Dukascopy price series to download.</summary>
public enum DukascopyPriceType
{
    /// <summary>BID candles (default).</summary>
    Bid,
    /// <summary>ASK candles.</summary>
    Ask,
    /// <summary>Mid-price: average of BID and ASK OHLC.</summary>
    Mid
}

/// <summary>
/// Downloads historical candle data from Dukascopy's free datafeed.
/// No API key required. Data is LZMA-compressed binary (.bi5 format).
/// Supports forex, gold, indices, and CFDs.
/// </summary>
public sealed class DukascopyDataProvider : IDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DukascopyDataProvider> _logger;
    private readonly DukascopyPriceType _priceType;

    public DukascopyDataProvider(
        HttpClient httpClient,
        ILogger<DukascopyDataProvider> logger,
        DukascopyPriceType priceType = DukascopyPriceType.Bid)
    {
        _httpClient = httpClient;
        _logger = logger;
        _priceType = priceType;
    }

    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pointSize = DukascopyHelpers.PointSizes.TryGetValue(symbol, out var ps) ? ps : 100_000m;
        var dates = DukascopyHelpers.BuildTradingDays(from.Date, to.Date);
        var priceLabel = _priceType.ToString();

        _logger.LogInformation("Dukascopy: fetching {Symbol} {PriceType} — {Count} trading days ({From:yyyy-MM-dd} to {To:yyyy-MM-dd})",
            symbol, priceLabel, dates.Count, from, to);

        List<BarRecord> allMinuteBars;

        if (_priceType == DukascopyPriceType.Mid)
        {
            allMinuteBars = await FetchMidPriceBarsAsync(symbol, dates, pointSize, ct);
        }
        else
        {
            allMinuteBars = await FetchSinglePriceBarsAsync(symbol, dates, pointSize, _priceType, ct);
        }

        // Sort by timestamp (parallel downloads may arrive out of order)
        allMinuteBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Detect partial opening bars per trading day (Req 7)
        int intervalMins = DukascopyHelpers.IntervalToMinutes(interval);
        if (intervalMins > 1 && allMinuteBars.Count > 0)
        {
            DateTime? lastWarnedDate = null;
            foreach (var bar in allMinuteBars)
            {
                var barDate = bar.Timestamp.UtcDateTime.Date;
                if (lastWarnedDate == barDate) continue;

                var windowBoundary = DukascopyHelpers.TruncateToInterval(bar.Timestamp, intervalMins);
                if (windowBoundary < bar.Timestamp)
                {
                    _logger.LogWarning(
                        "Dukascopy: partial opening bar for {Symbol} on {Date:yyyy-MM-dd} at {Interval} — " +
                        "window boundary {WindowBoundary:O} precedes first bar {FirstBar:O}",
                        symbol, barDate, interval, windowBoundary, bar.Timestamp);
                }
                lastWarnedDate = barDate;
            }
        }

        _logger.LogInformation("Dukascopy: {Count} minute bars, aggregating to {Interval}",
            allMinuteBars.Count, interval);

        var aggregated = DukascopyHelpers.Aggregate(allMinuteBars, interval, symbol);

        foreach (var bar in aggregated)
        {
            if (bar.Timestamp >= from && bar.Timestamp <= to)
                yield return bar;
        }
    }

    private async Task<List<BarRecord>> FetchSinglePriceBarsAsync(
        string symbol, List<DateTime> dates, decimal pointSize,
        DukascopyPriceType priceType, CancellationToken ct)
    {
        var priceLabel = priceType.ToString();
        var allMinuteBars = new List<BarRecord>();
        var uncachedDates = new List<DateTime>();

        foreach (var date in dates)
        {
            var cachePath = DukascopyHelpers.GetDayCachePath(CacheDir, symbol, priceLabel, date);
            if (DukascopyHelpers.IsCacheFileValid(cachePath))
            {
                var cached = DukascopyHelpers.LoadFromCsv(cachePath, symbol, "1m");
                allMinuteBars.AddRange(cached);
            }
            else
            {
                uncachedDates.Add(date);
            }
        }

        if (uncachedDates.Count > 0)
        {
            _logger.LogInformation("Dukascopy: {Cached} days from cache, {Uncached} to download",
                dates.Count - uncachedDates.Count, uncachedDates.Count);

            var batches = uncachedDates.Chunk(4);
            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                var tasks = batch.Select(d => FetchAndCacheDayAsync(symbol, d, pointSize, priceType, ct));
                var results = await Task.WhenAll(tasks);
                foreach (var dayBars in results)
                    allMinuteBars.AddRange(dayBars);
            }
        }

        return allMinuteBars;
    }

    private async Task<List<BarRecord>> FetchMidPriceBarsAsync(
        string symbol, List<DateTime> dates, decimal pointSize, CancellationToken ct)
    {
        var allMidBars = new List<BarRecord>();

        // For Mid, process each day: fetch both BID and ASK, compute average
        var batches = dates.Chunk(4);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = batch.Select(d => FetchMidDayAsync(symbol, d, pointSize, ct));
            var results = await Task.WhenAll(tasks);
            foreach (var dayBars in results)
                allMidBars.AddRange(dayBars);
        }

        return allMidBars;
    }

    private async Task<List<BarRecord>> FetchMidDayAsync(
        string symbol, DateTime date, decimal pointSize, CancellationToken ct)
    {
        // Check mid cache first
        var midCachePath = DukascopyHelpers.GetDayCachePath(CacheDir, symbol, "Mid", date);
        if (DukascopyHelpers.IsCacheFileValid(midCachePath))
            return DukascopyHelpers.LoadFromCsv(midCachePath, symbol, "1m");

        // Fetch both BID and ASK concurrently
        var bidTask = FetchAndCacheDayAsync(symbol, date, pointSize, DukascopyPriceType.Bid, ct);
        var askTask = FetchAndCacheDayAsync(symbol, date, pointSize, DukascopyPriceType.Ask, ct);
        await Task.WhenAll(bidTask, askTask);

        var bidBars = bidTask.Result;
        var askBars = askTask.Result;

        // If ASK fails but BID succeeds, treat as download failure (no partial mid-price)
        if (bidBars.Count > 0 && askBars.Count == 0)
        {
            _logger.LogWarning("Dukascopy Mid: ASK file missing for {Symbol} {Date:yyyy-MM-dd}, skipping day",
                symbol, date);
            return new();
        }

        if (bidBars.Count == 0) return new();

        // Build lookup from ASK bars by timestamp
        var askByTs = new Dictionary<DateTimeOffset, BarRecord>(askBars.Count);
        foreach (var ask in askBars)
            askByTs.TryAdd(ask.Timestamp, ask);

        var midBars = new List<BarRecord>(bidBars.Count);
        foreach (var bid in bidBars)
        {
            if (!askByTs.TryGetValue(bid.Timestamp, out var ask)) continue;

            midBars.Add(new BarRecord(
                symbol, "1m",
                (bid.Open + ask.Open) / 2m,
                (bid.High + ask.High) / 2m,
                (bid.Low + ask.Low) / 2m,
                (bid.Close + ask.Close) / 2m,
                bid.Volume, // volume from BID file
                bid.Timestamp));
        }

        // Cache mid bars
        if (midBars.Count > 0)
        {
            try { DukascopyHelpers.SaveToCsv(midCachePath, midBars); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to cache mid bars for {Symbol} {Date:yyyy-MM-dd}", symbol, date); }
        }

        return midBars;
    }

    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pointSize = DukascopyHelpers.PointSizes.TryGetValue(symbol, out var ps) ? ps : 100_000m;
        var dates = DukascopyHelpers.BuildTradingDays(from.Date, to.Date);

        _logger.LogInformation("Dukascopy ticks: fetching {Symbol} — {Count} trading days ({From:yyyy-MM-dd} to {To:yyyy-MM-dd})",
            symbol, dates.Count, from, to);

        foreach (var date in dates)
        {
            ct.ThrowIfCancellationRequested();

            // Build hour tasks for all 24 hours of the day
            var hourTasks = Enumerable.Range(0, 24)
                .Select(h => FetchHourTicksAsync(symbol, date, h, pointSize, ct))
                .ToArray();

            // Process in batches of 4 concurrent downloads (same as candle batches)
            var batches = hourTasks.Chunk(4);
            foreach (var batch in batches)
            {
                var results = await Task.WhenAll(batch);
                foreach (var hourTicks in results)
                {
                    foreach (var tick in hourTicks)
                    {
                        if (tick.Timestamp >= from && tick.Timestamp <= to)
                            yield return tick;
                    }
                }
            }
        }
    }

    private async Task<List<TickRecord>> FetchHourTicksAsync(
        string symbol, DateTime date, int hour, decimal pointSize, CancellationToken ct)
    {
        int month = date.Month - 1; // Dukascopy months are 0-indexed
        var url = $"{DukascopyHelpers.BaseUrl}/{symbol}/{date.Year}/{month:D2}/{date.Day:D2}/{hour:D2}h_ticks.bi5";

        byte[] compressed;
        try { compressed = await DownloadWithRetryAsync(url, ct); }
        catch (HttpRequestException)
        {
            return new();
        }

        if (compressed.Length < 13) return new();

        byte[] decompressed;
        try { decompressed = DukascopyHelpers.Decompress(compressed); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tick decompress failed for {Symbol} {Date:yyyy-MM-dd} hour {Hour}",
                symbol, date, hour);
            return new();
        }

        var hourStart = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);
        return DukascopyHelpers.ParseTicks(decompressed, hourStart, symbol, pointSize);
    }

    private async Task<List<BarRecord>> FetchAndCacheDayAsync(
        string symbol, DateTime date, decimal pointSize, DukascopyPriceType priceType, CancellationToken ct)
    {
        var url = DukascopyHelpers.BuildDayUrl(symbol, date, priceType);
        var priceLabel = priceType.ToString();

        byte[] compressed;
        try { compressed = await DownloadWithRetryAsync(url, ct); }
        catch (HttpRequestException)
        {
            _logger.LogDebug("No data for {Symbol} {PriceType} on {Date:yyyy-MM-dd}", symbol, priceLabel, date);
            return new();
        }

        if (compressed.Length < 13) return new();

        byte[] decompressed;
        try { decompressed = DukascopyHelpers.Decompress(compressed); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Decompress failed for {Symbol} {PriceType} {Date:yyyy-MM-dd}", symbol, priceLabel, date);
            return new();
        }

        var bars = DukascopyHelpers.ParseCandles(decompressed, date, symbol, pointSize);

        // Write to per-day cache
        if (bars.Count > 0)
        {
            try
            {
                var cachePath = DukascopyHelpers.GetDayCachePath(CacheDir, symbol, priceLabel, date);
                DukascopyHelpers.SaveToCsv(cachePath, bars);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cache {Symbol} {PriceType} {Date:yyyy-MM-dd}", symbol, priceLabel, date);
            }
        }

        return bars;
    }

    // --- HTTP Retry ---

    private ResiliencePipeline<byte[]> BuildRetryPipeline() =>
        new ResiliencePipelineBuilder<byte[]>()
            .AddRetry(new RetryStrategyOptions<byte[]>
            {
                MaxRetryAttempts = 3,
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                    TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))),
                ShouldHandle = new PredicateBuilder<byte[]>()
                    .Handle<HttpRequestException>(ex =>
                        ex.StatusCode is null || (int)ex.StatusCode >= 500),
                OnRetry = args =>
                {
                    _logger.LogDebug("Retry {Attempt}/3 after {Delay}s",
                        args.AttemptNumber + 1, args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

    /// <summary>
    /// Downloads a .bi5 file with Polly retry. Returns empty array for 404 (not retried).
    /// After exhausting retries, logs error and re-throws.
    /// </summary>
    private async Task<byte[]> DownloadWithRetryAsync(string url, CancellationToken ct)
    {
        var pipeline = BuildRetryPipeline();
        try
        {
            return await pipeline.ExecuteAsync(async token =>
            {
                var response = await _httpClient.GetAsync(url, token);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Array.Empty<byte>();

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(token);
            }, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<byte>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Dukascopy download failed after 3 retries: {Url} — {Message}", url, ex.Message);
            throw;
        }
    }

    // --- Per-Day CSV Cache ---

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TradingResearchEngine", "DukascopyCache");
}
