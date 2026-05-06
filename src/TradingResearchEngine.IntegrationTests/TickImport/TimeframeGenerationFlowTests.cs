using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration test: End-to-end cached ticks → generated CSV → DataFileRecord.
/// Validates: Requirements 4.1, 4.4, 4.5, 4.6
/// </summary>
public class TimeframeGenerationFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TickCacheService _cacheService;
    private readonly JsonTickImportRepository _importRepo;
    private readonly JsonGeneratedTimeframeRepository _timeframeRepo;
    private readonly InMemoryDataFileRepository _dataFileRepo;
    private readonly TimeframeGeneratorService _sut;

    public TimeframeGenerationFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tf-gen-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cacheDir = Path.Combine(_tempDir, "cache");
        var options = new TickImportOptions { CacheDirectory = cacheDir };
        _cacheService = new TickCacheService(Options.Create(options), NullLogger<TickCacheService>.Instance);
        _importRepo = new JsonTickImportRepository(Path.Combine(_tempDir, "imports"));
        _timeframeRepo = new JsonGeneratedTimeframeRepository(Path.Combine(_tempDir, "timeframes"));
        _dataFileRepo = new InMemoryDataFileRepository();

        _sut = new TimeframeGeneratorService(
            _cacheService,
            _importRepo,
            _timeframeRepo,
            _dataFileRepo,
            Options.Create(options),
            NullLogger<TimeframeGeneratorService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task EndToEnd_CachedTicks_GeneratesCsvAndRegistersRecords()
    {
        // Arrange: write tick data to cache
        var date = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var ticks = new List<TickCsvRow>();
        var baseTime = new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 120; i++) // 2 hours of minute-level ticks
        {
            ticks.Add(new TickCsvRow(
                baseTime.AddMinutes(i),
                1.08m + i * 0.00001m,
                1.0802m + i * 0.00001m,
                1.5m,
                2.0m));
        }
        await _cacheService.WriteDayTicksAsync("EURUSD", date, ticks);

        // Create a completed import record
        var importRecord = new TickImportRecord(
            ImportId: "tick-flow-001",
            Source: "Dukascopy",
            Symbol: "EURUSD",
            RequestedStart: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2023, 6, 13, 0, 0, 0, TimeSpan.Zero),
            Status: TickImportStatus.Completed,
            TotalTickCount: 120,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow);
        await _importRepo.SaveAsync(importRecord);

        // Act: generate 1H timeframe
        var result = await _sut.GenerateTimeframeAsync("tick-flow-001", "1H");

        // Assert: output file exists
        Assert.True(File.Exists(result.OutputFilePath));
        Assert.True(result.BarCount > 0);

        // Assert: DataFileRecord registered
        var dataFiles = await _dataFileRepo.ListAsync();
        Assert.Single(dataFiles);
        Assert.Equal("EURUSD", dataFiles[0].DetectedSymbol);
        Assert.Equal("1H", dataFiles[0].DetectedTimeframe);

        // Assert: GeneratedTimeframeRecord created
        var genRecords = await _timeframeRepo.ListByImportAsync("tick-flow-001");
        Assert.Single(genRecords);
        Assert.Equal("1H", genRecords[0].Timeframe);
        Assert.Equal(result.BarCount, genRecords[0].BarCount);

        // Assert: CSV content is valid
        var lines = File.ReadAllLines(result.OutputFilePath);
        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", lines[0]);
        Assert.Equal(result.BarCount + 1, lines.Length); // header + bars
    }

    [Fact]
    public async Task EndToEnd_DailyTimeframe_ProducesSingleBar()
    {
        // Arrange: write tick data for one day
        var date = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var ticks = new List<TickCsvRow>
        {
            new(new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero), 1.08m, 1.0802m, 1.5m, 2.0m),
            new(new DateTimeOffset(2023, 6, 12, 12, 0, 0, TimeSpan.Zero), 1.09m, 1.0902m, 2.0m, 2.5m),
            new(new DateTimeOffset(2023, 6, 12, 23, 59, 0, TimeSpan.Zero), 1.085m, 1.0852m, 1.0m, 1.5m),
        };
        await _cacheService.WriteDayTicksAsync("EURUSD", date, ticks);

        var importRecord = new TickImportRecord(
            ImportId: "tick-flow-002",
            Source: "Dukascopy",
            Symbol: "EURUSD",
            RequestedStart: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2023, 6, 13, 0, 0, 0, TimeSpan.Zero),
            Status: TickImportStatus.Completed,
            TotalTickCount: 3,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow);
        await _importRepo.SaveAsync(importRecord);

        // Act
        var result = await _sut.GenerateTimeframeAsync("tick-flow-002", "Daily");

        // Assert: single daily bar
        Assert.Equal(1, result.BarCount);
    }

    /// <summary>Simple in-memory IDataFileRepository for integration tests.</summary>
    private sealed class InMemoryDataFileRepository : IDataFileRepository
    {
        private readonly Dictionary<string, DataFileRecord> _store = new();

        public Task<DataFileRecord?> GetAsync(string fileId, CancellationToken ct = default)
            => Task.FromResult(_store.GetValueOrDefault(fileId));

        public Task<IReadOnlyList<DataFileRecord>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DataFileRecord>>(_store.Values.ToList());

        public Task SaveAsync(DataFileRecord record, CancellationToken ct = default)
        {
            _store[record.FileId] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string fileId, CancellationToken ct = default)
        {
            _store.Remove(fileId);
            return Task.CompletedTask;
        }
    }
}
