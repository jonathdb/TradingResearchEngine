using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

public class TimeframeGeneratorServiceTests : IDisposable
{
    private readonly Mock<ITickCacheService> _cacheMock = new();
    private readonly Mock<ITickImportRepository> _importRepoMock = new();
    private readonly Mock<IGeneratedTimeframeRepository> _timeframeRepoMock = new();
    private readonly Mock<IDataFileRepository> _dataFileRepoMock = new();
    private readonly TickImportOptions _options;
    private readonly TimeframeGeneratorService _sut;
    private readonly string _tempDir;

    public TimeframeGeneratorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tgs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new TickImportOptions { CacheDirectory = Path.Combine(_tempDir, "tick-cache") };
        Directory.CreateDirectory(Path.Combine(_tempDir, "generated"));

        _sut = new TimeframeGeneratorService(
            _cacheMock.Object,
            _importRepoMock.Object,
            _timeframeRepoMock.Object,
            _dataFileRepoMock.Object,
            Options.Create(_options),
            NullLogger<TimeframeGeneratorService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TickImportRecord CreateCompletedImport(string importId = "tick-abc123")
    {
        return new TickImportRecord(
            ImportId: importId,
            Source: "Dukascopy",
            Symbol: "EURUSD",
            RequestedStart: new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2023, 6, 2, 0, 0, 0, TimeSpan.Zero),
            Status: TickImportStatus.Completed,
            TotalTickCount: 1000,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow);
    }

    private static IAsyncEnumerable<TickCsvRow> CreateTickStream(int count = 10)
    {
        return GenerateTicksAsync(count);
    }

    private static async IAsyncEnumerable<TickCsvRow> GenerateTicksAsync(int count)
    {
        var baseTime = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new TickCsvRow(
                baseTime.AddMinutes(i),
                1.08m + i * 0.0001m,
                1.0802m + i * 0.0001m,
                1.5m,
                2.0m);
        }
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ValidRequest_GeneratesCorrectOutputFilename()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTickStream());
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Act
        var result = await _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Assert
        Assert.Contains("dukascopy_EURUSD_1H_20230601_20230602.csv", result.OutputFilePath);
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ValidRequest_RegistersDataFileRecord()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTickStream());
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Act
        await _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Assert
        _dataFileRepoMock.Verify(r => r.SaveAsync(
            It.Is<DataFileRecord>(df => df.DetectedSymbol == "EURUSD" && df.DetectedTimeframe == "1H"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ValidRequest_CreatesGeneratedTimeframeRecord()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTickStream());
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Act
        await _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Assert
        _timeframeRepoMock.Verify(r => r.SaveAsync(
            It.Is<GeneratedTimeframeRecord>(rec =>
                rec.TickImportId == "tick-abc123" && rec.Timeframe == "1H"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ExistingTimeframe_OverwritesAndUpdatesRecord()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTickStream());

        var existingRecord = new GeneratedTimeframeRecord(
            "gen-old", "tick-abc123", "1H", "/old/path.csv", "df-old", 100,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1));
        _timeframeRepoMock.Setup(r => r.ListByImportAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord> { existingRecord });

        // Act
        await _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Assert: old records deleted, new ones created
        _dataFileRepoMock.Verify(r => r.DeleteAsync("df-old", It.IsAny<CancellationToken>()), Times.Once);
        _timeframeRepoMock.Verify(r => r.DeleteAsync("gen-old", It.IsAny<CancellationToken>()), Times.Once);
        _dataFileRepoMock.Verify(r => r.SaveAsync(It.IsAny<DataFileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _timeframeRepoMock.Verify(r => r.SaveAsync(It.IsAny<GeneratedTimeframeRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ConcurrentGeneration_ThrowsInvalidOperationException()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Make tick reading slow
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(SlowTickStream());

        // Act: start first generation
        var task1 = _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Small delay to ensure first task acquires the lock
        await Task.Delay(50);

        // Assert: second generation throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GenerateTimeframeAsync("tick-abc123", "1H"));

        // Clean up first task
        try { await task1; } catch { }
    }

    [Fact]
    public async Task GenerateTimeframeAsync_EmptyTickStream_ThrowsInvalidOperationException()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyTickStream());
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GenerateTimeframeAsync("tick-abc123", "1H"));
    }

    [Fact]
    public async Task GenerateTimeframeAsync_ValidRequest_WritesToTempFileThenRenames()
    {
        // Arrange
        var import = CreateCompletedImport();
        _importRepoMock.Setup(r => r.GetAsync("tick-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(import);
        _cacheMock.Setup(c => c.ReadTicksAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTickStream());
        _timeframeRepoMock.Setup(r => r.ListByImportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GeneratedTimeframeRecord>());

        // Act
        var result = await _sut.GenerateTimeframeAsync("tick-abc123", "1H");

        // Assert: final file exists, no temp files remain
        Assert.True(File.Exists(result.OutputFilePath));
        var dir = Path.GetDirectoryName(result.OutputFilePath)!;
        var tempFiles = Directory.GetFiles(dir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    #region Helpers

    private static async IAsyncEnumerable<TickCsvRow> EmptyTickStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<TickCsvRow> SlowTickStream()
    {
        var baseTime = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await Task.Delay(200);
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            yield return new TickCsvRow(baseTime.AddMinutes(i), 1.08m, 1.0802m, 1.5m, 2.0m);
        }
    }

    #endregion
}
