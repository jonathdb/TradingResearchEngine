using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.MarketData;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Infrastructure.MarketData;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.MarketData;

public class MarketDataImportFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _importsDir;
    private readonly string _dataFilesDir;
    private readonly string _dataDir;
    private readonly JsonMarketDataImportRepository _importRepo;
    private readonly JsonDataFileRepository _dataFileRepo;
    private readonly MarketDataImportService _service;

    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

    public MarketDataImportFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-flow-test-{Guid.NewGuid():N}");
        _importsDir = Path.Combine(_tempDir, "imports");
        _dataFilesDir = Path.Combine(_tempDir, "datafiles");
        _dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_dataDir);

        _importRepo = new JsonMarketDataImportRepository(_importsDir);
        _dataFileRepo = new JsonDataFileRepository(_dataFilesDir);

        var mockProvider = new MockMarketDataProvider();

        _service = new MarketDataImportService(
            _importRepo, _dataFileRepo,
            new IMarketDataProvider[] { mockProvider },
            NullLogger<MarketDataImportService>.Instance,
            _dataDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task FullImport_MockProvider_CreatesValidDataFile()
    {
        var completed = new TaskCompletionSource<ImportCompletionUpdate>();
        _service.OnCompleted += update => completed.TrySetResult(update);

        var importId = await _service.StartImportAsync("MockProvider", "EURUSD", "1H", Start, End);

        var result = await Task.WhenAny(completed.Task, Task.Delay(10_000));
        Assert.Equal(completed.Task, result);

        var completion = await completed.Task;
        Assert.Equal(MarketDataImportStatus.Completed, completion.Status);

        // Verify import record
        var importRecord = await _importRepo.GetAsync(importId);
        Assert.NotNull(importRecord);
        Assert.Equal(MarketDataImportStatus.Completed, importRecord!.Status);
        Assert.NotNull(importRecord.OutputFilePath);
        Assert.NotNull(importRecord.OutputFileId);

        // Verify data file record
        var dataFiles = await _dataFileRepo.ListAsync();
        Assert.Single(dataFiles);
        Assert.Equal(ValidationStatus.Valid, dataFiles[0].ValidationStatus);
        Assert.Equal("EURUSD", dataFiles[0].DetectedSymbol);

        // Verify CSV file exists
        Assert.True(File.Exists(importRecord.OutputFilePath));
        var lines = File.ReadAllLines(importRecord.OutputFilePath);
        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", lines[0]);
        Assert.True(lines.Length > 1);
    }

    [Fact]
    public async Task FailedImport_PersistsFailureRecord()
    {
        var failProvider = new FailingMarketDataProvider();
        var failService = new MarketDataImportService(
            _importRepo, _dataFileRepo,
            new IMarketDataProvider[] { failProvider },
            NullLogger<MarketDataImportService>.Instance,
            _dataDir);

        var completed = new TaskCompletionSource<ImportCompletionUpdate>();
        failService.OnCompleted += update => completed.TrySetResult(update);

        var importId = await failService.StartImportAsync("FailProvider", "EURUSD", "1H", Start, End);

        var result = await Task.WhenAny(completed.Task, Task.Delay(10_000));
        Assert.Equal(completed.Task, result);

        var completion = await completed.Task;
        Assert.Equal(MarketDataImportStatus.Failed, completion.Status);
        Assert.Contains("Simulated failure", completion.ErrorMessage);

        var importRecord = await _importRepo.GetAsync(importId);
        Assert.NotNull(importRecord);
        Assert.Equal(MarketDataImportStatus.Failed, importRecord!.Status);
        Assert.Contains("Simulated failure", importRecord.ErrorDetail);

        // No data file should be created
        var dataFiles = await _dataFileRepo.ListAsync();
        Assert.Empty(dataFiles);

        failService.Dispose();
    }

    /// <summary>Mock provider that writes a valid CSV.</summary>
    private sealed class MockMarketDataProvider : IMarketDataProvider
    {
        public string SourceName => "MockProvider";

        public Task<IReadOnlyList<MarketSymbolInfo>> GetSupportedSymbolsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<MarketSymbolInfo>>(new[]
            {
                new MarketSymbolInfo("EURUSD", "Euro / US Dollar", new[] { "1H", "Daily" })
            });
        }

        public Task<CsvWriteResult> DownloadToFileAsync(
            string symbol, string timeframe,
            DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
            string outputPath, IProgressReporter? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(1, 2, "Downloading day 1 of 2");
            progress?.Report(2, 2, "Downloading day 2 of 2");

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(outputPath,
                "Timestamp,Open,High,Low,Close,Volume\n" +
                "2024-01-02T00:00:00+00:00,1.1050,1.1060,1.1040,1.1055,1000\n" +
                "2024-01-02T01:00:00+00:00,1.1055,1.1070,1.1045,1.1065,1100\n");

            return Task.FromResult(new CsvWriteResult(
                outputPath, symbol, timeframe,
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 1, 0, 0, TimeSpan.Zero),
                2));
        }
    }

    /// <summary>Mock provider that always fails.</summary>
    private sealed class FailingMarketDataProvider : IMarketDataProvider
    {
        public string SourceName => "FailProvider";

        public Task<IReadOnlyList<MarketSymbolInfo>> GetSupportedSymbolsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<MarketSymbolInfo>>(new[]
            {
                new MarketSymbolInfo("EURUSD", "Euro / US Dollar", new[] { "1H" })
            });
        }

        public Task<CsvWriteResult> DownloadToFileAsync(
            string symbol, string timeframe,
            DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
            string outputPath, IProgressReporter? progress = null,
            CancellationToken ct = default)
        {
            throw new Exception("Simulated failure: network timeout");
        }
    }
}
