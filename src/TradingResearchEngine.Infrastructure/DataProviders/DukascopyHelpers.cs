using System.Buffers.Binary;
using System.Globalization;
using SharpCompress.Compressors.LZMA;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Shared static helpers for Dukascopy data: decompression, binary parsing,
/// bar aggregation, point sizes, and CSV read/write. Used by both
/// <see cref="DukascopyDataProvider"/> (inline backtest) and the import provider.
/// </summary>
public static class DukascopyHelpers
{
    /// <summary>Dukascopy datafeed base URL.</summary>
    public const string BaseUrl = "https://datafeed.dukascopy.com/datafeed";

    /// <summary>Point size divisors for converting integer prices to decimals.</summary>
    public static readonly Dictionary<string, decimal> PointSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EURUSD"] = 100_000m, ["GBPUSD"] = 100_000m, ["USDJPY"] = 1_000m,
        ["USDCHF"] = 100_000m, ["AUDUSD"] = 100_000m, ["NZDUSD"] = 100_000m,
        ["USDCAD"] = 100_000m, ["EURGBP"] = 100_000m, ["EURJPY"] = 1_000m,
        ["GBPJPY"] = 1_000m, ["XAUUSD"] = 1_000m, ["XAGUSD"] = 100_000m,
        ["USA500IDXUSD"] = 10m, ["USA30IDXUSD"] = 10m, ["USATECHIDXUSD"] = 10m,
    };

    /// <summary>Decompresses LZMA-compressed Dukascopy .bi5 data.</summary>
    public static byte[] Decompress(byte[] data)
    {
        byte[] props = data[..5];
        long uncompressedSize = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8));
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

    /// <summary>Parses decompressed binary candle data into BarRecords.</summary>
    public static List<BarRecord> ParseCandles(
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

            var ts = new DateTimeOffset(dayStart, TimeSpan.Zero).AddSeconds(ms);
            decimal o = op / pointSize, h = hi / pointSize, l = lo / pointSize, c = cl / pointSize;

            if (o > 0 && h > 0 && l > 0 && c > 0 && h >= l)
                bars.Add(new BarRecord(symbol, "1m", o, h, l, c, (decimal)vol, ts));
        }
        return bars;
    }

    /// <summary>
    /// Parses decompressed binary tick data into TickRecords.
    /// Dukascopy provides top-of-book data only (one bid, one ask, no depth).
    /// <c>LastTrade</c> is synthesized from the mid-price <c>(ask + bid) / 2</c>
    /// with size <c>Math.Min(askVol, bidVol)</c>. This is provider-derived,
    /// not exchange-reported — Dukascopy does not publish actual trade prints.
    /// </summary>
    public static List<TickRecord> ParseTicks(
        byte[] data, DateTime hourStart, string symbol, decimal pointSize)
    {
        const int Size = 20;
        var ticks = new List<TickRecord>(data.Length / Size);

        for (int i = 0; i + Size <= data.Length; i += Size)
        {
            uint ms = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4));
            uint askRaw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 4, 4));
            uint bidRaw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 8, 4));
            float askVol = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(i + 12, 4)));
            float bidVol = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(i + 16, 4)));

            decimal ask = askRaw / pointSize;
            decimal bid = bidRaw / pointSize;

            if (ask <= 0 || bid <= 0) continue;

            var ts = new DateTimeOffset(hourStart, TimeSpan.Zero).AddMilliseconds(ms);
            decimal midPrice = (ask + bid) / 2m;
            decimal tradeSize = Math.Min((decimal)askVol, (decimal)bidVol);

            ticks.Add(new TickRecord(
                symbol,
                new[] { new BidLevel(bid, (decimal)bidVol) },
                new[] { new AskLevel(ask, (decimal)askVol) },
                new LastTrade(midPrice, tradeSize, ts),
                ts));
        }

        return ticks;
    }

    /// <summary>Aggregates minute bars to the target interval using time-based boundaries.</summary>
    public static List<BarRecord> Aggregate(List<BarRecord> bars, string interval, string symbol)
    {
        int mins = IntervalToMinutes(interval);
        if (mins == 1) return bars;

        var result = new List<BarRecord>();
        if (bars.Count == 0) return result;

        int i = 0;
        while (i < bars.Count)
        {
            // Compute the time boundary for this aggregation window
            var windowStart = TruncateToInterval(bars[i].Timestamp, mins);
            var windowEnd = windowStart.AddMinutes(mins);

            decimal open = bars[i].Open;
            decimal high = Math.Max(bars[i].High, bars[i].Open);
            decimal low = Math.Min(bars[i].Low, bars[i].Open);
            decimal close = bars[i].Close;
            decimal volume = bars[i].Volume;
            var timestamp = windowStart;

            i++;
            while (i < bars.Count && bars[i].Timestamp < windowEnd)
            {
                if (bars[i].High > high) high = bars[i].High;
                if (bars[i].Low < low) low = bars[i].Low;
                close = bars[i].Close;
                volume += bars[i].Volume;
                i++;
            }

            // Clamp high/low to cover the final close value
            high = Math.Max(high, close);
            low = Math.Min(low, close);

            result.Add(new BarRecord(symbol, interval, open, high, low, close, volume, timestamp));
        }
        return result;
    }

    /// <summary>Truncates a timestamp to the nearest interval boundary (UTC-safe).</summary>
    internal static DateTimeOffset TruncateToInterval(DateTimeOffset ts, int intervalMinutes)
    {
        // Use UtcDateTime to avoid local timezone offset issues
        var utc = ts.UtcDateTime;
        long totalMinutes = (long)(utc - utc.Date).TotalMinutes;
        long truncated = totalMinutes / intervalMinutes * intervalMinutes;
        return new DateTimeOffset(utc.Date, TimeSpan.Zero).AddMinutes(truncated);
    }

    /// <summary>Converts an interval string to minutes.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="interval"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="interval"/> is not a recognized value.</exception>
    public static int IntervalToMinutes(string interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return interval.ToLowerInvariant() switch
        {
            "1m" => 1, "5m" => 5, "15m" => 15, "30m" => 30,
            "1h" or "60m" => 60, "4h" => 240, "1d" or "daily" => 1440,
            var unknown => throw new ArgumentException(
                $"Unrecognized interval '{unknown}'. Supported values: 1m, 5m, 15m, 30m, 1h, 60m, 4h, 1d, daily.",
                nameof(interval))
        };
    }

    /// <summary>Builds the list of trading days (skipping weekends) in a date range.</summary>
    public static List<DateTime> BuildTradingDays(DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        var current = startDate;
        while (current <= endDate)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                dates.Add(current);
            current = current.AddDays(1);
        }
        return dates;
    }

    /// <summary>Builds the Dukascopy URL for a specific day's BID minute candles.</summary>
    public static string BuildDayUrl(string symbol, DateTime date)
        => BuildDayUrl(symbol, date, DukascopyPriceType.Bid);

    /// <summary>Builds the Dukascopy URL for a specific day's minute candles for the given price type.</summary>
    public static string BuildDayUrl(string symbol, DateTime date, DukascopyPriceType priceType)
    {
        int month = date.Month - 1; // Dukascopy months are 0-indexed
        string file = priceType == DukascopyPriceType.Ask
            ? "ASK_candles_min_1.bi5"
            : "BID_candles_min_1.bi5";
        return $"{BaseUrl}/{symbol}/{date.Year}/{month:D2}/{date.Day:D2}/{file}";
    }

    /// <summary>
    /// Returns the per-day cache file path for a symbol, price type, and date.
    /// Creates the directory structure if it does not exist.
    /// </summary>
    public static string GetDayCachePath(string cacheDir, string symbol, string priceType, DateTime date)
    {
        var path = Path.Combine(cacheDir, symbol, priceType,
            date.Year.ToString("D4"), date.Month.ToString("D2"), $"{date.Day:D2}.csv");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return path;
    }

    /// <summary>
    /// Returns true if a cache file exists and contains data beyond just a header row.
    /// Zero-byte or header-only files are treated as missing.
    /// </summary>
    public static bool IsCacheFileValid(string path)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length == 0) return false;
        // Header-only CSV: "Timestamp,Open,High,Low,Close,Volume\r\n" is ~44 bytes
        // Any file with data rows will be larger than 60 bytes
        return info.Length > 60;
    }

    /// <summary>Writes bars to a CSV file in canonical engine format.</summary>
    public static void SaveToCsv(string path, List<BarRecord> bars)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path);
        writer.WriteLine("Timestamp,Open,High,Low,Close,Volume");
        foreach (var b in bars)
        {
            writer.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0:O},{1},{2},{3},{4},{5}",
                b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume));
        }
    }

    /// <summary>Loads bars from a canonical CSV file.</summary>
    public static List<BarRecord> LoadFromCsv(string path, string symbol, string interval)
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
                    decimal.Parse(parts[1], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[2], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[5], CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture)));
            }
            catch { /* skip malformed */ }
        }
        return bars;
    }
}
