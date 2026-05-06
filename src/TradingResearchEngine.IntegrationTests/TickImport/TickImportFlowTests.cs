using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration test: Full import with mocked HTTP → cache files created.
/// Validates: Requirements 1.3, 1.4
/// </summary>
public class TickImportFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TickCacheService _cacheService;
    private readonly JsonTickImportRepository _importRepo;
    private readonly Mock<ITickDownloader> _downloaderMock = new();
    private readonly TickImportService _sut;

    public TickImportFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tick-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = new TickImportOptions { CacheDirectory = Path.Combine(_tempDir, "cache") };
        _cacheService = new TickCacheService(Options.Create(options), NullLogger<TickCacheService>.Instance);
        _importRepo = new JsonTickImportRepository(Path.Combine(_tempDir, "imports"));

        _downloaderMock.Setup(d => d.SupportedSymbols)
            .Returns(new HashSet<string> { "EURUSD", "GBPUSD", "USDJPY", "XAUUSD" });

        _sut = new TickImportService(
            _cacheService,
            _importRepo,
            _downloaderMock.Object,
            Options.Create(options),
            NullLogger<TickImportService>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task FullImport_WithMockedDownloader_CreatesCacheFiles()
    {
        // Arrange: mock downloader returns ticks for one day
        var date = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc); // Monday
        var ticks = new List<TickCsvRow>
        {
            new(new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero), 1.08m, 1.0802m, 1.5m, 2.0m),
            new(new DateTimeOffset(2023, 6, 12, 0, 1, 0, TimeSpan.Zero), 1.081m, 1.0812m, 2.0m, 2.5m),
            new(new DateTimeOffset(2023, 6, 12, 1, 0, 0, TimeSpan.Zero), 1.082m, 1.0822m, 1.0m, 1.5m),
        };

        _downloaderMock.Setup(d => d.DownloadAsync(
                "EURUSD",
                It.IsAny<IReadOnlyList<DateTime>>(),
                It.IsAny<IProgress<(int, int)>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateDownloadStream(date, ticks));

        var completionTcs = new TaskCompletionSource<TickImportCompletionUpdate>();
        _sut.OnCompleted += update => completionTcs.TrySetResult(update);

        // Act
        var importId = await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 13, 0, 0, 0, TimeSpan.Zero));

        // Wait for completion
        var completion = await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(TickImportStatus.Completed, completion.Status);

        // Verify cache file was created
        var cachePath = Path.Combine(_tempDir, "cache", "EURUSD", "ticks", "2023", "06", "12.csv");
        Assert.True(File.Exists(cachePath));

        // Verify tick count in record
        var record = await _importRepo.GetAsync(importId);
        Assert.NotNull(record);
        Assert.Equal(TickImportStatus.Completed, record!.Status);
        Assert.True(record.TotalTickCount > 0);
    }

    [Fact]
    public async Task FullImport_AllDaysCached_CompletesImmediately()
    {
        // Arrange: pre-cache the day (Monday June 12)
        var date = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var ticks = new List<TickCsvRow>
        {
            new(new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero), 1.08m, 1.0802m, 1.5m, 2.0m),
        };
        await _cacheService.WriteDayTicksAsync("EURUSD", date, ticks);

        // Act: request range that only covers the single cached day
        // Use start=June 12, end=June 12 (same day) so only that weekday is checked
        var importId = await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 12, 23, 59, 59, TimeSpan.Zero));

        // Assert: completed immediately, no download called
        var record = await _importRepo.GetAsync(importId);
        Assert.NotNull(record);
        Assert.Equal(TickImportStatus.Completed, record!.Status);
        _downloaderMock.Verify(d => d.DownloadAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(),
            It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async IAsyncEnumerable<TickDownloadItem> CreateDownloadStream(
        DateTime date, List<TickCsvRow> ticks)
    {
        await Task.Yield();
        // Simulate downloading hour 0 and hour 1
        var hour0Ticks = ticks.Where(t => t.Timestamp.Hour == 0).ToList();
        var hour1Ticks = ticks.Where(t => t.Timestamp.Hour == 1).ToList();

        yield return new TickDownloadItem(date, 0, hour0Ticks);
        yield return new TickDownloadItem(date, 1, hour1Ticks);
    }
}
