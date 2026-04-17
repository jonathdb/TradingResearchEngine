using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Reads bar or tick records from a local CSV file.
/// Skips malformed rows and increments <see cref="MalformedRecordCount"/>.
/// </summary>
public sealed class CsvDataProvider : IDataProvider
{
    private readonly string _filePath;
    private readonly ILogger<CsvDataProvider> _logger;

    /// <summary>Number of rows skipped due to parse errors.</summary>
    public int MalformedRecordCount { get; private set; }

    /// <inheritdoc cref="CsvDataProvider"/>
    public CsvDataProvider(string filePath, ILogger<CsvDataProvider> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// Opens the CSV file for reading, throwing a descriptive error if the file does not exist.
    /// </summary>
    private StreamReader OpenFileOrThrow()
    {
        if (!File.Exists(_filePath))
        {
            throw new FileNotFoundException(
                $"Data file not found: '{_filePath}'. " +
                "Ensure the file exists in the project data directory (./data/) " +
                "or check the DataProvider.FilePath configuration.",
                _filePath);
        }
        return new StreamReader(_filePath);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = OpenFileOrThrow();
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        });

        // Read header
        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            BarRecord? bar = null;
            try
            {
                var open = decimal.Parse(csv.GetField("Open")!, CultureInfo.InvariantCulture);
                var high = decimal.Parse(csv.GetField("High")!, CultureInfo.InvariantCulture);
                var low = decimal.Parse(csv.GetField("Low")!, CultureInfo.InvariantCulture);
                var close = decimal.Parse(csv.GetField("Close")!, CultureInfo.InvariantCulture);
                var volume = decimal.Parse(csv.GetField("Volume")!, CultureInfo.InvariantCulture);
                var timestamp = DateTimeOffset.Parse(csv.GetField("Timestamp")!, CultureInfo.InvariantCulture);
                bar = new BarRecord(symbol, interval, open, high, low, close, volume, timestamp);
            }
            catch (Exception ex)
            {
                MalformedRecordCount++;
                _logger.LogWarning(ex, "Skipping malformed CSV row.");
                continue;
            }

            if (bar.Timestamp >= from && bar.Timestamp <= to)
                yield return bar;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = OpenFileOrThrow();
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        });

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            TickRecord? tick = null;
            try
            {
                var ts = DateTimeOffset.Parse(csv.GetField("Timestamp")!, CultureInfo.InvariantCulture);
                tick = new TickRecord(
                    symbol,
                    new[] { new BidLevel(decimal.Parse(csv.GetField("BidPrice")!, CultureInfo.InvariantCulture),
                                         decimal.Parse(csv.GetField("BidSize")!, CultureInfo.InvariantCulture)) },
                    new[] { new AskLevel(decimal.Parse(csv.GetField("AskPrice")!, CultureInfo.InvariantCulture),
                                         decimal.Parse(csv.GetField("AskSize")!, CultureInfo.InvariantCulture)) },
                    new LastTrade(decimal.Parse(csv.GetField("LastPrice")!, CultureInfo.InvariantCulture),
                                  decimal.Parse(csv.GetField("LastVolume")!, CultureInfo.InvariantCulture), ts),
                    ts);
            }
            catch (Exception ex)
            {
                MalformedRecordCount++;
                _logger.LogWarning(ex, "Skipping malformed CSV tick row.");
                continue;
            }

            if (tick.Timestamp >= from && tick.Timestamp <= to)
                yield return tick;
        }
    }
}
