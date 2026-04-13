using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.MarketData;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.Infrastructure.MarketData;

/// <summary>
/// Downloads historical candle data from Dukascopy's free datafeed and writes
/// canonical CSV files. Caches raw minute bars per (symbol, date) so that
/// re-imports and different timeframes reuse previously downloaded data.
/// </summary>
public sealed class DukascopyImportProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DukascopyImportProvider> _logger;
    private readonly string _cacheDir;

    private const int MaxRetries = 3;
    private const int MaxConcurrentDownloads = 8;

    private static readonly string[] AllTimeframes =
        { "1m", "5m", "15m", "30m", "1H", "4H", "Daily" };

    private static readonly MarketSymbolInfo[] SupportedSymbols = new[]
    {
        new MarketSymbolInfo("EURUSD", "Euro / US Dollar", AllTimeframes),
        new MarketSymbolInfo("GBPUSD", "British Pound / US Dollar", AllTimeframes),
        new MarketSymbolInfo("USDJPY", "US Dollar / Japanese Yen", AllTimeframes),
        new MarketSymbolInfo("USDCHF", "US Dollar / Swiss Franc", AllTimeframes),
        new MarketSymbolInfo("AUDUSD", "Australian Dollar / US Dollar", AllTimeframes),
        new MarketSymbolInfo("NZDUSD", "New Zealand Dollar / US Dollar", AllTimeframes),
        new MarketSymbolInfo("USDCAD", "US Dollar / Canadian Dollar", AllTimeframes),
        new MarketSymbolInfo("EURGBP", "Euro / British Pound", AllTimeframes),
        new MarketSymbolInfo("EURJPY", "Euro / Japanese Yen", AllTimeframes),
        new MarketSymbolInfo("GBPJPY", "British Pound / Japanese Yen", AllTimeframes),
        new MarketSymbolInfo("XAUUSD", "Gold / US Dollar", AllTimeframes),
        new MarketSymbolInfo("XAGUSD", "Silver / US Dollar", AllTimeframes),
        new MarketSymbolInfo("USA500IDXUSD", "S&P 500 Index", AllTimeframes),
        new MarketSymbolInfo("USA30IDXUSD", "Dow Jones 30 Index", AllTimeframes),
        new MarketSymbolInfo("USATECHIDXUSD", "Nasdaq 100 Index", AllTimeframes),
    };

    /// <summary>Creates the provider with optional cache directory override.</summary>
    public DukascopyImportProvider(
        HttpClient httpClient,
        ILogger<DukascopyImportProvider> logger,
        string? cacheDir = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TradingResearchEngine", "DukascopyDayCache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <inheritdoc/>
    public string SourceName => "Dukascopy";

    /// <inheritdoc/>
    public Task<IReadOnlyList<MarketSymbolInfo>> GetSupportedSymbolsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MarketSymbolInfo>>(SupportedSymbols);

    /// <inheritdoc/>
    public async Task<CsvWriteResult> DownloadToFileAsync(
        string symbol, string timeframe,
        DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        string outputPath,
        IProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        if (!DukascopyHelpers.PointSizes.ContainsKey(symbol))
            throw new ArgumentException($"Unsupported symbol: {symbol}", nameof(symbol));

        var pointSize = DukascopyHelpers.PointSizes[symbol];
        var dates = DukascopyHelpers.BuildTradingDays(requestedStart.Date, requestedEnd.Date.AddDays(-1));
        int totalChunks = dates.Count;

        _logger.LogInformation(
            "DukascopyImport: starting {Symbol} {Timeframe} — {Count} trading days ({Start:yyyy-MM-dd} to {End:yyyy-MM-dd})",
            symbol, timeframe, totalChunks, requestedStart, requestedEnd);

        var allMinuteBars = new List<BarRecord>();
        int completed = 0;

        // Parallel download in batches, with per-day cache
        var batches = dates.Chunk(MaxConcurrentDownloads);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = batch.Select(d => FetchDayCachedAsync(symbol, d, pointSize, ct));
            var results = await Task.WhenAll(tasks);
            foreach (var dayBars in results)
                allMinuteBars.AddRange(dayBars);
            completed += batch.Length;
            progress?.Report(completed, totalChunks, $"Downloading day {completed} of {totalChunks}");
        }

        allMinuteBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("DukascopyImport: {Cached}/{Total} days from cache, aggregating {Count} minute bars to {Timeframe}",
            dates.Count(d => File.Exists(GetDayCachePath(symbol, d))), totalChunks,
            allMinuteBars.Count, timeframe);

        var aggregated = DukascopyHelpers.Aggregate(allMinuteBars, timeframe, symbol);

        // Filter to requested range (start inclusive, end exclusive)
        var filtered = aggregated
            .Where(b => b.Timestamp >= requestedStart && b.Timestamp < requestedEnd)
            .ToList();

        DukascopyHelpers.SaveToCsv(outputPath, filtered);

        if (filtered.Count == 0)
            return new CsvWriteResult(outputPath, symbol, timeframe, requestedStart, requestedEnd, 0);

        return new CsvWriteResult(
            outputPath, symbol, timeframe,
            filtered[0].Timestamp, filtered[^1].Timestamp, filtered.Count);
    }

    /// <summary>
    /// Loads minute bars for a single day from cache if available,
    /// otherwise downloads from Dukascopy and caches the result.
    /// </summary>
    private async Task<List<BarRecord>> FetchDayCachedAsync(
        string symbol, DateTime date, decimal pointSize, CancellationToken ct)
    {
        var cachePath = GetDayCachePath(symbol, date);

        // Try cache first
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = DukascopyHelpers.LoadFromCsv(cachePath, symbol, "1m");
                if (cached.Count > 1)
                {
                    _logger.LogDebug("Cache hit: {Symbol} {Date:yyyy-MM-dd} ({Count} bars)", symbol, date, cached.Count);
                    return cached;
                }

                // A single-bar cache file is likely stale daily data from before
                // the interval fix — invalidate and re-download.
                if (cached.Count <= 1)
                {
                    _logger.LogDebug("Stale cache for {Symbol} {Date:yyyy-MM-dd} ({Count} bar), re-downloading",
                        symbol, date, cached.Count);
                }
            }
            catch
            {
                // Corrupted cache file — re-download
                _logger.LogDebug("Corrupted cache for {Symbol} {Date:yyyy-MM-dd}, re-downloading", symbol, date);
            }
        }

        // Download with retry
        var bars = await FetchDayWithRetryAsync(symbol, date, pointSize, ct);

        // Cache the result (even if empty — avoids re-downloading holidays)
        try { DukascopyHelpers.SaveToCsv(cachePath, bars); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to cache {Symbol} {Date:yyyy-MM-dd}", symbol, date); }

        return bars;
    }

    private string GetDayCachePath(string symbol, DateTime date)
        => Path.Combine(_cacheDir, $"{symbol}_{date:yyyyMMdd}_1m.csv");

    private async Task<List<BarRecord>> FetchDayWithRetryAsync(
        string symbol, DateTime date, decimal pointSize, CancellationToken ct)
    {
        var url = DukascopyHelpers.BuildDayUrl(symbol, date);

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var compressed = await _httpClient.GetByteArrayAsync(url, ct);
                if (compressed.Length < 13) return new();

                var decompressed = DukascopyHelpers.Decompress(compressed);
                return DukascopyHelpers.ParseCandles(decompressed, date, symbol, pointSize);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogDebug("Retry {Attempt}/{Max} for {Symbol} {Date:yyyy-MM-dd} after {Delay}s",
                    attempt + 1, MaxRetries, symbol, date, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException)
            {
                _logger.LogDebug("No data for {Symbol} on {Date:yyyy-MM-dd} after {Max} retries",
                    symbol, date, MaxRetries);
                return new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching {Symbol} {Date:yyyy-MM-dd}", symbol, date);
                return new();
            }
        }

        return new();
    }
}
