using System.Buffers.Binary;
using System.Globalization;
using SharpCompress.Compressors.LZMA;
using TradingResearchEngine.Core.DataHandling;

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

            var ts = new DateTimeOffset(dayStart, TimeSpan.Zero).AddMilliseconds(ms);
            decimal o = op / pointSize, h = hi / pointSize, l = lo / pointSize, c = cl / pointSize;

            if (o > 0 && h > 0 && l > 0 && c > 0)
                bars.Add(new BarRecord(symbol, "1m", o, h, l, c, (decimal)vol, ts));
        }
        return bars;
    }

    /// <summary>Aggregates minute bars to the target interval.</summary>
    public static List<BarRecord> Aggregate(List<BarRecord> bars, string interval, string symbol)
    {
        int mins = IntervalToMinutes(interval);
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

    /// <summary>Converts an interval string to minutes.</summary>
    public static int IntervalToMinutes(string interval) => interval.ToLowerInvariant() switch
    {
        "1m" => 1, "5m" => 5, "15m" => 15, "30m" => 30,
        "1h" or "60m" => 60, "4h" => 240, "1d" or "daily" => 1440,
        _ => 1440
    };

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
    {
        int month = date.Month - 1; // Dukascopy months are 0-indexed
        return $"{BaseUrl}/{symbol}/{date.Year}/{month:D2}/{date.Day:D2}/BID_candles_min_1.bi5";
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
