using System.Globalization;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.MarketData;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.Infrastructure.MarketData;

/// <summary>
/// Downloads historical candle data from Dukascopy's free datafeed and writes
/// canonical CSV files. Implements <see cref="IMarketDataProvider"/> for the
/// market data import workflow. Reuses shared helpers from <see cref="DukascopyHelpers"/>.
/// </summary>
public sealed class DukascopyImportProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DukascopyImportProvider> _logger;

    private const int MaxRetries = 3;

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

    /// <inheritdoc cref="DukascopyImportProvider"/>
    public DukascopyImportProvider(HttpClient httpClient, ILogger<DukascopyImportProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string SourceName => "Dukascopy";

    /// <inheritdoc/>
    public Task<IReadOnlyList<MarketSymbolInfo>> GetSupportedSymbolsAsync(
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MarketSymbolInfo>>(SupportedSymbols);
    }

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
        for (int i = 0; i < dates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dayBars = await FetchDayWithRetryAsync(symbol, dates[i], pointSize, ct);
            allMinuteBars.AddRange(dayBars);
            progress?.Report(i + 1, totalChunks, $"Downloading day {i + 1} of {totalChunks}");
        }

        allMinuteBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("DukascopyImport: downloaded {Count} minute bars, aggregating to {Timeframe}",
            allMinuteBars.Count, timeframe);

        var aggregated = DukascopyHelpers.Aggregate(allMinuteBars, timeframe, symbol);

        // Filter to requested range (start inclusive, end exclusive)
        var filtered = aggregated
            .Where(b => b.Timestamp >= requestedStart && b.Timestamp < requestedEnd)
            .ToList();

        // Write canonical CSV
        DukascopyHelpers.SaveToCsv(outputPath, filtered);

        if (filtered.Count == 0)
        {
            return new CsvWriteResult(outputPath, symbol, timeframe,
                requestedStart, requestedEnd, 0);
        }

        return new CsvWriteResult(
            outputPath, symbol, timeframe,
            filtered[0].Timestamp, filtered[^1].Timestamp, filtered.Count);
    }

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
