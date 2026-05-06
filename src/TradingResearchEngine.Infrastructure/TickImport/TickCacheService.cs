using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.Infrastructure.TickImport;

/// <summary>
/// Implements <see cref="ITickCacheService"/> using per-day CSV files at:
/// <c>{CacheDir}/{Symbol}/ticks/{yyyy}/{MM}/{dd}.csv</c>.
/// </summary>
public sealed class TickCacheService : ITickCacheService
{
    private const string CsvHeader = "Timestamp,Bid,Ask,BidVolume,AskVolume";

    private readonly string _cacheDir;
    private readonly ILogger<TickCacheService> _logger;

    /// <summary>Initializes a new instance of <see cref="TickCacheService"/>.</summary>
    /// <param name="options">Tick import configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public TickCacheService(IOptions<TickImportOptions> options, ILogger<TickCacheService> logger)
    {
        _cacheDir = options.Value.CacheDirectory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DateTime>> GetMissingDaysAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var missing = new List<DateTime>();
        var current = startDate.Date;
        var end = endDate.Date;

        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                var path = GetDayFilePath(symbol, current);
                if (!File.Exists(path))
                {
                    missing.Add(current);
                }
            }
            current = current.AddDays(1);
        }

        _logger.LogDebug("GetMissingDays for {Symbol} [{Start:yyyy-MM-dd}..{End:yyyy-MM-dd}]: {Count} missing",
            symbol, startDate, endDate, missing.Count);

        return Task.FromResult<IReadOnlyList<DateTime>>(missing);
    }

    /// <inheritdoc/>
    public Task<(DateTime Earliest, DateTime Latest)?> GetCoverageAsync(
        string symbol, CancellationToken ct = default)
    {
        var ticksDir = Path.Combine(_cacheDir, symbol, "ticks");
        if (!Directory.Exists(ticksDir))
        {
            return Task.FromResult<(DateTime Earliest, DateTime Latest)?>(null);
        }

        DateTime? earliest = null;
        DateTime? latest = null;

        // Scan year/month/day directory structure
        foreach (var yearDir in Directory.GetDirectories(ticksDir))
        {
            var yearName = Path.GetFileName(yearDir);
            if (!int.TryParse(yearName, out var year)) continue;

            foreach (var monthDir in Directory.GetDirectories(yearDir))
            {
                var monthName = Path.GetFileName(monthDir);
                if (!int.TryParse(monthName, out var month)) continue;

                foreach (var dayFile in Directory.GetFiles(monthDir, "*.csv"))
                {
                    var dayName = Path.GetFileNameWithoutExtension(dayFile);
                    if (!int.TryParse(dayName, out var day)) continue;

                    try
                    {
                        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                        if (earliest is null || date < earliest) earliest = date;
                        if (latest is null || date > latest) latest = date;
                    }
                    catch
                    {
                        // Skip invalid date combinations
                    }
                }
            }
        }

        if (earliest is null || latest is null)
        {
            return Task.FromResult<(DateTime Earliest, DateTime Latest)?>(null);
        }

        return Task.FromResult<(DateTime Earliest, DateTime Latest)?>((earliest.Value, latest.Value));
    }

    /// <inheritdoc/>
    public Task WriteDayTicksAsync(
        string symbol, DateTime date, IReadOnlyList<TickCsvRow> ticks, CancellationToken ct = default)
    {
        var path = GetDayFilePath(symbol, date);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine(CsvHeader);
        foreach (var tick in ticks)
        {
            writer.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4}",
                tick.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                tick.Bid,
                tick.Ask,
                tick.BidVolume,
                tick.AskVolume));
        }

        _logger.LogDebug("Wrote {Count} ticks for {Symbol} on {Date:yyyy-MM-dd}", ticks.Count, symbol, date);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TickCsvRow> ReadTicksAsync(
        string symbol, DateTime startDate, DateTime endDate,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var current = startDate.Date;
        var end = endDate.Date;

        while (current <= end)
        {
            if (ct.IsCancellationRequested) yield break;

            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                var path = GetDayFilePath(symbol, current);
                if (File.Exists(path))
                {
                    await foreach (var tick in ReadDayFileAsync(path, ct))
                    {
                        yield return tick;
                    }
                }
            }
            current = current.AddDays(1);
        }
    }

    /// <inheritdoc/>
    public Task<long> GetTickCountAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        long count = 0;
        var current = startDate.Date;
        var end = endDate.Date;

        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                var path = GetDayFilePath(symbol, current);
                if (File.Exists(path))
                {
                    // Count lines minus header
                    var lines = File.ReadAllLines(path);
                    count += Math.Max(0, lines.Length - 1);
                }
            }
            current = current.AddDays(1);
        }

        return Task.FromResult(count);
    }

    /// <summary>Gets the file path for a specific symbol and date.</summary>
    internal string GetDayFilePath(string symbol, DateTime date)
    {
        return Path.Combine(
            _cacheDir,
            symbol,
            "ticks",
            date.Year.ToString("D4"),
            date.Month.ToString("D2"),
            $"{date.Day:D2}.csv");
    }

    private static async IAsyncEnumerable<TickCsvRow> ReadDayFileAsync(
        string path, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(path);

        // Skip header
        await reader.ReadLineAsync(ct);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (ct.IsCancellationRequested) yield break;

            var parts = line.Split(',');
            if (parts.Length < 5) continue;

            TickCsvRow tick;
            try
            {
                tick = new TickCsvRow(
                    DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    decimal.Parse(parts[1], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[2], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3], CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4], CultureInfo.InvariantCulture));
            }
            catch
            {
                continue; // Skip malformed rows
            }

            yield return tick;
        }
    }
}
