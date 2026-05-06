using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration test: Delete generated file removes records, keeps tick cache.
/// Validates: Requirements 11.3
/// </summary>
public class DeletionFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TickCacheService _cacheService;
    private readonly JsonGeneratedTimeframeRepository _timeframeRepo;
    private readonly InMemoryDataFileRepository _dataFileRepo;

    public DeletionFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"del-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cacheDir = Path.Combine(_tempDir, "cache");
        var options = new TickImportOptions { CacheDirectory = cacheDir };
        _cacheService = new TickCacheService(Options.Create(options), NullLogger<TickCacheService>.Instance);
        _timeframeRepo = new JsonGeneratedTimeframeRepository(Path.Combine(_tempDir, "timeframes"));
        _dataFileRepo = new InMemoryDataFileRepository();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DeleteGeneratedFile_RemovesRecords_KeepsTickCache()
    {
        // Arrange: create tick cache
        var date = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var ticks = new List<TickCsvRow>
        {
            new(new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero), 1.08m, 1.0802m, 1.5m, 2.0m),
        };
        await _cacheService.WriteDayTicksAsync("EURUSD", date, ticks);

        // Create generated file
        var generatedDir = Path.Combine(_tempDir, "generated");
        Directory.CreateDirectory(generatedDir);
        var generatedFilePath = Path.Combine(generatedDir, "dukascopy_EURUSD_1H_20230612_20230613.csv");
        await File.WriteAllTextAsync(generatedFilePath, "Timestamp,Open,High,Low,Close,Volume\n2023-06-12T00:00:00+00:00,1.08,1.08,1.08,1.08,1.5\n");

        // Register records
        var dataFileRecord = new DataFileRecord(
            FileId: "df-001",
            FileName: "dukascopy_EURUSD_1H_20230612_20230613.csv",
            FilePath: generatedFilePath,
            DetectedSymbol: "EURUSD",
            DetectedTimeframe: "1H",
            FirstBar: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            LastBar: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            BarCount: 1,
            ValidationStatus: ValidationStatus.Valid,
            ValidationError: null,
            AddedAt: DateTimeOffset.UtcNow);
        await _dataFileRepo.SaveAsync(dataFileRecord);

        var genRecord = new GeneratedTimeframeRecord(
            RecordId: "gen-001",
            TickImportId: "tick-abc",
            Timeframe: "1H",
            OutputFilePath: generatedFilePath,
            OutputFileId: "df-001",
            BarCount: 1,
            FirstBar: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            LastBar: new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            GeneratedAt: DateTimeOffset.UtcNow);
        await _timeframeRepo.SaveAsync(genRecord);

        // Act: simulate deletion of generated file
        // Delete the generated file
        if (File.Exists(generatedFilePath))
            File.Delete(generatedFilePath);

        // Delete the DataFileRecord
        await _dataFileRepo.DeleteAsync("df-001");

        // Delete the GeneratedTimeframeRecord
        await _timeframeRepo.DeleteAsync("gen-001");

        // Assert: generated file and records are gone
        Assert.False(File.Exists(generatedFilePath));
        Assert.Null(await _dataFileRepo.GetAsync("df-001"));
        Assert.Null(await _timeframeRepo.GetAsync("gen-001"));

        // Assert: tick cache is still intact
        var cachePath = Path.Combine(_tempDir, "cache", "EURUSD", "ticks", "2023", "06", "12.csv");
        Assert.True(File.Exists(cachePath));

        // Verify ticks can still be read
        var readTicks = new List<TickCsvRow>();
        await foreach (var tick in _cacheService.ReadTicksAsync("EURUSD", date, date))
        {
            readTicks.Add(tick);
        }
        Assert.Single(readTicks);
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
