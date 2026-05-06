using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.DataFiles;

namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Reads cached tick data and produces aggregated bar CSV files at a requested timeframe.
/// Registers the output as a DataFileRecord and creates a GeneratedTimeframeRecord.
/// </summary>
public sealed class TimeframeGeneratorService
{
    /// <summary>Supported timeframe strings mapped to their interval in minutes.</summary>
    private static readonly IReadOnlyDictionary<string, int> SupportedTimeframes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 1,
            ["5m"] = 5,
            ["15m"] = 15,
            ["30m"] = 30,
            ["1H"] = 60,
            ["4H"] = 240,
            ["Daily"] = 1440
        };

    private readonly ITickCacheService _cacheService;
    private readonly ITickImportRepository _importRepository;
    private readonly IGeneratedTimeframeRepository _timeframeRepository;
    private readonly IDataFileRepository _dataFileRepository;
    private readonly TickImportOptions _options;
    private readonly ILogger<TimeframeGeneratorService> _logger;
    private readonly ConcurrentDictionary<string, bool> _activeGenerations = new();

    /// <summary>Initializes a new instance of <see cref="TimeframeGeneratorService"/>.</summary>
    public TimeframeGeneratorService(
        ITickCacheService cacheService,
        ITickImportRepository importRepository,
        IGeneratedTimeframeRepository timeframeRepository,
        IDataFileRepository dataFileRepository,
        IOptions<TickImportOptions> options,
        ILogger<TimeframeGeneratorService> logger)
    {
        _cacheService = cacheService;
        _importRepository = importRepository;
        _timeframeRepository = timeframeRepository;
        _dataFileRepository = dataFileRepository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a bar CSV at the specified timeframe from the tick data
    /// associated with the given import.
    /// </summary>
    /// <param name="tickImportId">The ID of the completed tick import.</param>
    /// <param name="timeframe">The target timeframe (1m, 5m, 15m, 30m, 1H, 4H, Daily).</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generation result with output file details.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the import is not found, not completed, or a generation is already running.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the timeframe is not supported.</exception>
    public async Task<GenerationResult> GenerateTimeframeAsync(
        string tickImportId, string timeframe,
        IProgress<(int current, int total)>? progress = null,
        CancellationToken ct = default)
    {
        // Validate timeframe
        if (!SupportedTimeframes.TryGetValue(timeframe, out var intervalMinutes))
            throw new ArgumentException($"Timeframe '{timeframe}' is not supported. Supported: {string.Join(", ", SupportedTimeframes.Keys)}");

        // Load import record
        var importRecord = await _importRepository.GetAsync(tickImportId, ct)
            ?? throw new InvalidOperationException($"Tick import '{tickImportId}' not found.");

        if (importRecord.Status != TickImportStatus.Completed)
            throw new InvalidOperationException($"Tick import '{tickImportId}' is not completed (status: {importRecord.Status}).");

        // Prevent concurrent generation for same import
        var generationKey = $"{tickImportId}:{timeframe}";
        if (!_activeGenerations.TryAdd(generationKey, true))
            throw new InvalidOperationException("Generation already in progress for this import.");

        try
        {
            return await ExecuteGenerationAsync(importRecord, timeframe, intervalMinutes, progress, ct);
        }
        finally
        {
            _activeGenerations.TryRemove(generationKey, out _);
        }
    }

    private async Task<GenerationResult> ExecuteGenerationAsync(
        TickImportRecord importRecord,
        string timeframe,
        int intervalMinutes,
        IProgress<(int current, int total)>? progress,
        CancellationToken ct)
    {
        var symbol = importRecord.Symbol;
        var startDate = importRecord.RequestedStart.UtcDateTime.Date;
        var endDate = importRecord.RequestedEnd.UtcDateTime.Date;

        _logger.LogInformation(
            "Generating {Timeframe} bars for {Symbol} ({Start:yyyy-MM-dd} to {End:yyyy-MM-dd})",
            timeframe, symbol, startDate, endDate);

        // Read ticks and aggregate into bars
        var bars = await AggregateToBarsAsync(symbol, startDate, endDate, intervalMinutes, ct);

        if (bars.Count == 0)
            throw new InvalidOperationException("No tick data available for the requested range.");

        // Build output filename
        var startStr = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var endStr = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var fileName = $"dukascopy_{symbol}_{timeframe}_{startStr}_{endStr}.csv";

        // Determine output directory (use data directory)
        var outputDir = Path.Combine(_options.CacheDirectory, "..", "generated");
        Directory.CreateDirectory(outputDir);
        var finalPath = Path.GetFullPath(Path.Combine(outputDir, fileName));

        // Write to temp file first, then atomic rename
        var tempPath = finalPath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await WriteBarsToFileAsync(tempPath, bars, ct);

            // Atomic rename (overwrite if exists)
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        // Register DataFileRecord
        var fileId = $"df-{Guid.NewGuid():N}";
        var dataFileRecord = new DataFileRecord(
            FileId: fileId,
            FileName: fileName,
            FilePath: finalPath,
            DetectedSymbol: symbol,
            DetectedTimeframe: timeframe,
            FirstBar: bars[0].Timestamp,
            LastBar: bars[^1].Timestamp,
            BarCount: bars.Count,
            ValidationStatus: ValidationStatus.Valid,
            ValidationError: null,
            AddedAt: DateTimeOffset.UtcNow);

        // Check for existing generated timeframe record and update
        var existingRecords = await _timeframeRepository.ListByImportAsync(importRecord.ImportId, ct);
        var existingForTimeframe = existingRecords.FirstOrDefault(
            r => string.Equals(r.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase));

        if (existingForTimeframe is not null)
        {
            // Update existing records
            await _dataFileRepository.DeleteAsync(existingForTimeframe.OutputFileId, ct);
            await _timeframeRepository.DeleteAsync(existingForTimeframe.RecordId, ct);
        }

        await _dataFileRepository.SaveAsync(dataFileRecord, ct);

        // Create GeneratedTimeframeRecord
        var recordId = $"gen-{Guid.NewGuid():N}";
        var generatedRecord = new GeneratedTimeframeRecord(
            RecordId: recordId,
            TickImportId: importRecord.ImportId,
            Timeframe: timeframe,
            OutputFilePath: finalPath,
            OutputFileId: fileId,
            BarCount: bars.Count,
            FirstBar: bars[0].Timestamp,
            LastBar: bars[^1].Timestamp,
            GeneratedAt: DateTimeOffset.UtcNow);

        await _timeframeRepository.SaveAsync(generatedRecord, ct);

        _logger.LogInformation(
            "Generated {BarCount} {Timeframe} bars for {Symbol} → {FilePath}",
            bars.Count, timeframe, symbol, finalPath);

        return new GenerationResult(
            OutputFilePath: finalPath,
            OutputFileId: fileId,
            BarCount: bars.Count,
            FirstBar: bars[0].Timestamp,
            LastBar: bars[^1].Timestamp);
    }

    private async Task<List<BarData>> AggregateToBarsAsync(
        string symbol, DateTime startDate, DateTime endDate,
        int intervalMinutes, CancellationToken ct)
    {
        var bars = new List<BarData>();
        BarData? currentBar = null;
        DateTimeOffset currentWindowStart = default;

        await foreach (var tick in _cacheService.ReadTicksAsync(symbol, startDate, endDate, ct))
        {
            var windowStart = TruncateToInterval(tick.Timestamp, intervalMinutes);

            if (currentBar is null || windowStart != currentWindowStart)
            {
                // Start a new bar
                if (currentBar is not null)
                    bars.Add(currentBar);

                currentWindowStart = windowStart;
                currentBar = new BarData
                {
                    Timestamp = windowStart,
                    Open = tick.Bid,
                    High = tick.Bid,
                    Low = tick.Bid,
                    Close = tick.Bid,
                    Volume = tick.BidVolume
                };
            }
            else
            {
                // Update current bar
                if (tick.Bid > currentBar.High) currentBar.High = tick.Bid;
                if (tick.Bid < currentBar.Low) currentBar.Low = tick.Bid;
                currentBar.Close = tick.Bid;
                currentBar.Volume += tick.BidVolume;
            }
        }

        // Don't forget the last bar
        if (currentBar is not null)
            bars.Add(currentBar);

        return bars;
    }

    /// <summary>
    /// Truncates a timestamp to the nearest interval boundary aligned to UTC midnight.
    /// </summary>
    private static DateTimeOffset TruncateToInterval(DateTimeOffset timestamp, int intervalMinutes)
    {
        var utc = timestamp.ToUniversalTime();
        var minutesSinceMidnight = (int)(utc.TimeOfDay.TotalMinutes);
        var truncatedMinutes = (minutesSinceMidnight / intervalMinutes) * intervalMinutes;
        return new DateTimeOffset(
            utc.Date.AddMinutes(truncatedMinutes),
            TimeSpan.Zero);
    }

    private static async Task WriteBarsToFileAsync(
        string filePath, List<BarData> bars, CancellationToken ct)
    {
        await using var writer = new StreamWriter(filePath, append: false, encoding: System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("Timestamp,Open,High,Low,Close,Volume");

        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                bar.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume);
            await writer.WriteLineAsync(line);
        }
    }

    /// <summary>Internal mutable bar accumulator used during aggregation.</summary>
    private sealed class BarData
    {
        public DateTimeOffset Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }
}
