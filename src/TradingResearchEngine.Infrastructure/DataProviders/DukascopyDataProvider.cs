using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SharpCompress.Compressors.LZMA;
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
    private const string BaseUrl = "https://datafeed.dukascopy.com/datafeed";

    private static readonly Dictionary<string, decimal> PointSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EURUSD"] = 100_000m, ["GBPUSD"] = 100_000m, ["USDJPY"] = 1_000m,
        ["USDCHF"] = 100_000m, ["AUDUSD"] = 100_000m, ["NZDUSD"] = 100_000m,
        ["USDCAD"] = 100_000m, ["EURGBP"] = 100_000m, ["EURJPY"] = 1_000m,
        ["GBPJPY"] = 1_000m, ["XAUUSD"] = 1_000m, ["XAGUSD"] = 100_000m,
        ["USA500IDXUSD"] = 10m, ["USA30IDXUSD"] = 10m, ["USATECHIDXUSD"] = 10m,
    };

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
            var cached = LoadFromCsv(cachePath, symbol, interval);
            foreach (var bar in cached)
            {
                if (bar.Timestamp >= from && bar.Timestamp <= to)
                    yield return bar;
            }
            yield break;
        }

        var pointSize = PointSizes.TryGetValue(symbol, out var ps) ? ps : 100_000m;
        var currentDate = from.Date;
        var endDate = to.Date;

        // Build list of dates to fetch (skip weekends for forex)
        var dates = new List<DateTime>();
        while (currentDate <= endDate)
        {
            if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                dates.Add(currentDate);
            currentDate = currentDate.AddDays(1);
        }

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

        var aggregated = Aggregate(allMinuteBars, interval, symbol);

        // Save to cache
        SaveToCsv(cachePath, aggregated);
        _logger.LogInformation("Dukascopy: cached {Count} bars to {Path}", aggregated.Count, cachePath);

        foreach (var bar in aggregated)
        {
            if (bar.Timestamp >= from && bar.Timestamp <= to)
                yield return bar;
        }
    }

    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogWarning("Dukascopy tick data not yet implemented. Use bar data.");
        yield break;
    }

    private async Task<List<BarRecord>> FetchDayAsync(
        string symbol, DateTime date, decimal pointSize, CancellationToken ct)
    {
        int month = date.Month - 1; // Dukascopy months are 0-indexed
        var url = $"{BaseUrl}/{symbol}/{date.Year}/{month:D2}/{date.Day:D2}/BID_candles_min_1.bi5";

        byte[] compressed;
        try { compressed = await _httpClient.GetByteArrayAsync(url, ct); }
        catch (HttpRequestException)
        {
            _logger.LogDebug("No data for {Symbol} on {Date:yyyy-MM-dd}", symbol, date);
            return new();
        }

        if (compressed.Length < 13) return new();

        byte[] decompressed;
        try { decompressed = Decompress(compressed); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Decompress failed for {Symbol} {Date:yyyy-MM-dd}", symbol, date);
            return new();
        }

        return ParseCandles(decompressed, date, symbol, pointSize);
    }

    private static byte[] Decompress(byte[] data)
    {
        byte[] props = data[..5];
        long uncompressedSize = BitConverter.ToInt64(data, 5);
        if (uncompressedSize <= 0 || uncompressedSize > 50_000_000)
            return Array.Empty<byte>();

        using var input = new MemoryStream(data, 13, data.Length - 13);
        using var output = new MemoryStream((int)uncompressedSize);
        using var lzma = new LzmaStream(props, input, data.Length - 13, uncompressedSize);

        byte[] buffer = new byte[8192];
        int totalRead = 0;
        while (totalRead < uncompressedSize)
        {
            int read = lzma.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            output.Write(buffer, 0, read);
            totalRead += read;
        }
        return output.ToArray();
    }

    private static List<BarRecord> ParseCandles(
        byte[] data, DateTime dayStart, string symbol, decimal pointSize)
    {
        const int Size = 24;
        var bars = new List<BarRecord>(data.Length / Size);

        for (int i = 0; i + Size <= data.Length; i += Size)
        {
            var rec = new byte[Size];
            Array.Copy(data, i, rec, 0, Size);

            int ms = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(0, 4));
            int op = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(4, 4));
            int hi = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(8, 4));
            int lo = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(12, 4));
            int cl = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(16, 4));
            int vb = BinaryPrimitives.ReadInt32BigEndian(rec.AsSpan(20, 4));
            float vol = BitConverter.Int32BitsToSingle(vb);

            var ts = new DateTimeOffset(dayStart, TimeSpan.Zero).AddMilliseconds(ms);
            decimal o = op / pointSize, h = hi / pointSize, l = lo / pointSize, c = cl / pointSize;

            if (o > 0 && h > 0 && l > 0 && c > 0)
                bars.Add(new BarRecord(symbol, "1m", o, h, l, c, (decimal)vol, ts));
        }
        return bars;
    }

    private static List<BarRecord> Aggregate(List<BarRecord> bars, string interval, string symbol)
    {
        int mins = interval.ToLowerInvariant() switch
        {
            "1m" => 1, "5m" => 5, "15m" => 15, "30m" => 30,
            "1h" or "60m" => 60, "4h" => 240, "1d" or "daily" => 1440,
            _ => 1440
        };
        if (mins == 1) return bars;

        var result = new List<BarRecord>();
        for (int i = 0; i < bars.Count; i += mins)
        {
            int end = Math.Min(i + mins, bars.Count);
            if (i >= end) break;

            decimal open = bars[i].Open;
            decimal high = bars[i].High;
            decimal low = bars[i].Low;
            decimal close = bars[end - 1].Close;
            decimal volume = 0m;

            for (int j = i; j < end; j++)
            {
                if (bars[j].High > high) high = bars[j].High;
                if (bars[j].Low < low) low = bars[j].Low;
                volume += bars[j].Volume;
            }

            result.Add(new BarRecord(symbol, interval, open, high, low, close, volume, bars[i].Timestamp));
        }
        return result;
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

    private static void SaveToCsv(string path, List<BarRecord> bars)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path);
        writer.WriteLine("Timestamp,Open,High,Low,Close,Volume");
        foreach (var b in bars)
        {
            writer.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:O},{1},{2},{3},{4},{5}",
                b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume));
        }
    }

    private static List<BarRecord> LoadFromCsv(string path, string symbol, string interval)
    {
        var bars = new List<BarRecord>();
        using var reader = new StreamReader(path);
        reader.ReadLine(); // skip header

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length < 6) continue;
            try
            {
                bars.Add(new BarRecord(symbol, interval,
                    decimal.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)));
            }
            catch { /* skip malformed */ }
        }
        return bars;
    }
}
