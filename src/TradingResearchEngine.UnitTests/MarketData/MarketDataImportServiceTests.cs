using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.MarketData;
using TradingResearchEngine.Application.Research;
using Xunit;

namespace TradingResearchEngine.UnitTests.MarketData;

public class MarketDataImportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IMarketDataImportRepository> _importRepo;
    private readonly Mock<IDataFileRepository> _dataFileRepo;
    private readonly Mock<IMarketDataProvider> _provider;
    private readonly MarketDataImportService _service;

    private static readonly DateTimeOffset Start = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public MarketDataImportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-svc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _importRepo = new Mock<IMarketDataImportRepository>();
        _dataFileRepo = new Mock<IDataFileRepository>();
        _provider = new Mock<IMarketDataProvider>();

        _provider.Setup(p => p.SourceName).Returns("Dukascopy");
        _provider.Setup(p => p.GetSupportedSymbolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketSymbolInfo>
            {
                new("EURUSD", "Euro / US Dollar", new[] { "1H", "Daily" })
            });

        _importRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketDataImportRecord>());

        _service = new MarketDataImportService(
            _importRepo.Object, _dataFileRepo.Object,
            new[] { _provider.Object },
            NullLogger<MarketDataImportService>.Instance,
            _tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StartImport_InvalidRange_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartImportAsync("Dukascopy", "EURUSD", "1H", End, Start));
    }

    [Fact]
    public async Task StartImport_UnknownSource_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartImportAsync("UnknownSource", "EURUSD", "1H", Start, End));
    }

    [Fact]
    public async Task StartImport_UnsupportedSymbol_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartImportAsync("Dukascopy", "INVALID", "1H", Start, End));
    }

    [Fact]
    public async Task StartImport_CreatesRunningRecord()
    {
        SetupSuccessfulDownload();

        var importId = await _service.StartImportAsync("Dukascopy", "EURUSD", "1H", Start, End);

        Assert.NotNull(importId);
        _importRepo.Verify(r => r.SaveAsync(
            It.Is<MarketDataImportRecord>(rec =>
                rec.Status == MarketDataImportStatus.Running &&
                rec.Symbol == "EURUSD"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartImport_WhileRunning_ThrowsInvalidOperation()
    {
        // Setup a provider that blocks indefinitely
        _provider.Setup(p => p.DownloadToFileAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(), It.IsAny<IProgressReporter>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string s, string t, DateTimeOffset rs, DateTimeOffset re,
                string o, IProgressReporter? p, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return new CsvWriteResult(o, s, t, rs, re, 100);
            });

        await _service.StartImportAsync("Dukascopy", "EURUSD", "1H", Start, End);

        // Second start should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartImportAsync("Dukascopy", "EURUSD", "Daily", Start, End));
    }

    [Fact]
    public async Task StartImport_Success_CreatesDataFileRecord()
    {
        SetupSuccessfulDownload();
        var completed = new TaskCompletionSource<ImportCompletionUpdate>();
        _service.OnCompleted += update => completed.TrySetResult(update);

        // Allow GetAsync to return the saved record for the duplicate check
        _importRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                new MarketDataImportRecord(id, "Dukascopy", "EURUSD", "1H", Start, End,
                    MarketDataImportStatus.Running, CreatedAt: DateTimeOffset.UtcNow));

        await _service.StartImportAsync("Dukascopy", "EURUSD", "1H", Start, End);

        var result = await Task.WhenAny(completed.Task, Task.Delay(5000));
        Assert.Equal(completed.Task, result);
        var completionResult = await completed.Task;
        Assert.Equal(MarketDataImportStatus.Completed, completionResult.Status);

        _dataFileRepo.Verify(r => r.SaveAsync(
            It.Is<DataFileRecord>(df => df.DetectedSymbol == "EURUSD"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartImport_ProviderFails_SetsFailedStatus()
    {
        _provider.Setup(p => p.DownloadToFileAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(), It.IsAny<IProgressReporter>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        _importRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                new MarketDataImportRecord(id, "Dukascopy", "EURUSD", "1H", Start, End,
                    MarketDataImportStatus.Running, CreatedAt: DateTimeOffset.UtcNow));

        var completed = new TaskCompletionSource<ImportCompletionUpdate>();
        _service.OnCompleted += update => completed.TrySetResult(update);

        await _service.StartImportAsync("Dukascopy", "EURUSD", "1H", Start, End);

        var result = await Task.WhenAny(completed.Task, Task.Delay(5000));
        Assert.Equal(completed.Task, result);
        var completionResult = await completed.Task;
        Assert.Equal(MarketDataImportStatus.Failed, completionResult.Status);
        Assert.Contains("Network error", completionResult.ErrorMessage);
    }

    [Fact]
    public async Task RecoverOnStartup_ResetsRunningToFailed()
    {
        var runningRecord = new MarketDataImportRecord(
            "imp-orphan", "Dukascopy", "EURUSD", "1H", Start, End,
            MarketDataImportStatus.Running, CreatedAt: DateTimeOffset.UtcNow);

        _importRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketDataImportRecord> { runningRecord });

        await _service.RecoverOnStartupAsync();

        _importRepo.Verify(r => r.SaveAsync(
            It.Is<MarketDataImportRecord>(rec =>
                rec.ImportId == "imp-orphan" &&
                rec.Status == MarketDataImportStatus.Failed &&
                rec.ErrorDetail == "Interrupted by application restart"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverOnStartup_DeletesOrphanedTmpFiles()
    {
        var tmpFile = Path.Combine(_tempDir, "test.csv.tmp");
        await File.WriteAllTextAsync(tmpFile, "orphaned data");

        _importRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketDataImportRecord>());

        await _service.RecoverOnStartupAsync();

        Assert.False(File.Exists(tmpFile));
    }

    [Fact]
    public void GetActiveImport_NoImport_ReturnsNull()
    {
        Assert.Null(_service.GetActiveImport());
    }

    private void SetupSuccessfulDownload()
    {
        _provider.Setup(p => p.DownloadToFileAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(), It.IsAny<IProgressReporter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string s, string t, DateTimeOffset rs, DateTimeOffset re,
                string outputPath, IProgressReporter? p, CancellationToken ct) =>
            {
                // Write a minimal CSV so the file exists for rename
                File.WriteAllText(outputPath, "Timestamp,Open,High,Low,Close,Volume\n");
                return new CsvWriteResult(outputPath, s, t, rs, re, 100);
            });
    }
}
