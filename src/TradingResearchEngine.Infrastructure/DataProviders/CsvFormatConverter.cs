using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Converts common CSV export formats (Yahoo Finance, TradingView, MetaTrader)
/// to the engine's standard format: Timestamp,Open,High,Low,Close,Volume
/// </summary>
public static class CsvFormatConverter
{
    /// <summary>Supported source formats.</summary>
    public enum SourceFormat
    {
        /// <summary>Auto-detect from headers.</summary>
        Auto,
        /// <summary>Yahoo Finance download: Date,Open,High,Low,Close,Adj Close,Volume</summary>
        YahooFinance,
        /// <summary>TradingView export: time,open,high,low,close,Volume</summary>
        TradingView,
        /// <summary>MetaTrader export: Date,Time,Open,High,Low,Close,Volume</summary>
        MetaTrader,
        /// <summary>
        /// QuantDataManager export: Date,Time,Open,High,Low,Close,Volume
        /// Date format is yyyy.MM.dd and Time format is HH:mm (no seconds).
        /// </summary>
        QuantDataManager,
        /// <summary>Already in engine format: Timestamp,Open,High,Low,Close,Volume</summary>
        Engine
    }

    /// <summary>
    /// Converts a CSV file from a known format to the engine's standard format.
    /// Returns the converted CSV content as a string.
    /// </summary>
    public static string Convert(string csvContent, SourceFormat format = SourceFormat.Auto, string timezoneId = "UTC")
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return csvContent;

        if (format == SourceFormat.Auto)
            format = DetectFormat(lines);

        if (format == SourceFormat.Engine)
            return csvContent; // already correct

        var output = new List<string> { "Timestamp,Open,High,Low,Close,Volume" };

        for (int i = 1; i < lines.Length; i++)
        {
            var converted = ConvertLine(lines[i], format, timezoneId);
            if (converted is not null)
                output.Add(converted);
        }

        return string.Join('\n', output);
    }

    /// <summary>Converts a source file on disk and writes the result to the output path.</summary>
    public static void ConvertFile(string inputPath, string outputPath, SourceFormat format = SourceFormat.Auto, string timezoneId = "UTC")
    {
        var content = File.ReadAllText(inputPath);
        var converted = Convert(content, format, timezoneId);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, converted);
    }

    /// <summary>Detects the source format from the header and optional data rows.</summary>
    public static SourceFormat DetectFormat(string[] lines)
    {
        var lower = lines[0].ToLowerInvariant().Trim();

        if (lower.StartsWith("timestamp,open,high,low,close,volume"))
            return SourceFormat.Engine;
        if (lower.Contains("adj close") || lower.StartsWith("date,open,high,low,close,adj"))
            return SourceFormat.YahooFinance;
        if (lower.StartsWith("time,open") || lower.StartsWith("\"time\",\"open\""))
            return SourceFormat.TradingView;
        if (lower.Contains("date") && lower.Contains("time") && !lower.Contains("timestamp"))
        {
            // Peek at first data row to distinguish QDM (yyyy.MM.dd) from MT5 (yyyy-MM-dd)
            if (lines.Length > 1 && lines[1].Length > 4 && lines[1][4] == '.')
                return SourceFormat.QuantDataManager;
            return SourceFormat.MetaTrader;
        }

        return SourceFormat.Engine; // assume engine format if unrecognised
    }

    /// <summary>Detects the source format from the header line (backward-compatible overload).</summary>
    public static SourceFormat DetectFormat(string headerLine)
        => DetectFormat(new[] { headerLine });

    private static string? ConvertLine(string line, SourceFormat format, string timezoneId = "UTC")
    {
        try
        {
            return format switch
            {
                SourceFormat.YahooFinance => ConvertYahoo(line),
                SourceFormat.TradingView => ConvertTradingView(line),
                SourceFormat.MetaTrader => ConvertMetaTrader(line),
                SourceFormat.QuantDataManager => ConvertQuantDataManager(line, timezoneId),
                _ => null
            };
        }
        catch { return null; }
    }

    // Yahoo: Date,Open,High,Low,Close,Adj Close,Volume
    private static string ConvertYahoo(string line)
    {
        var p = line.Split(',');
        if (p.Length < 7) throw new FormatException();
        var ts = DateTimeOffset.Parse(p[0].Trim(), CultureInfo.InvariantCulture);
        return $"{ts:O},{p[1].Trim()},{p[2].Trim()},{p[3].Trim()},{p[4].Trim()},{p[6].Trim()}";
    }

    // TradingView: time,open,high,low,close,Volume (time is unix timestamp or ISO)
    private static string ConvertTradingView(string line)
    {
        var p = line.Replace("\"", "").Split(',');
        if (p.Length < 6) throw new FormatException();
        DateTimeOffset ts;
        if (long.TryParse(p[0].Trim(), out var unix))
            ts = DateTimeOffset.FromUnixTimeSeconds(unix);
        else
            ts = DateTimeOffset.Parse(p[0].Trim(), CultureInfo.InvariantCulture);
        return $"{ts:O},{p[1].Trim()},{p[2].Trim()},{p[3].Trim()},{p[4].Trim()},{p[5].Trim()}";
    }

    // MetaTrader: Date,Time,Open,High,Low,Close,Volume
    private static string ConvertMetaTrader(string line)
    {
        var p = line.Split(',');
        if (p.Length < 7) throw new FormatException();
        var ts = DateTimeOffset.Parse($"{p[0].Trim()} {p[1].Trim()}", CultureInfo.InvariantCulture);
        return $"{ts:O},{p[2].Trim()},{p[3].Trim()},{p[4].Trim()},{p[5].Trim()},{p[6].Trim()}";
    }

    // QuantDataManager: Date,Time,Open,High,Low,Close,Volume (Date is yyyy.MM.dd, Time is HH:mm)
    private static string ConvertQuantDataManager(string line, string timezoneId = "UTC")
    {
        var p = line.Split(',');
        if (p.Length < 7) throw new FormatException();

        var datePart = p[0].Trim().Replace('.', '-');   // 2020.01.02 → 2020-01-02
        var timePart = p[1].Trim();                      // 00:00

        // Parse the naive datetime, then attach the correct timezone offset
        var naive = DateTime.ParseExact(
            $"{datePart} {timePart}",
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var offset = tz.GetUtcOffset(naive);
        var ts = new DateTimeOffset(naive, offset);

        return $"{ts:O},{p[2].Trim()},{p[3].Trim()},{p[4].Trim()},{p[5].Trim()},{p[6].Trim()}";
    }
}
