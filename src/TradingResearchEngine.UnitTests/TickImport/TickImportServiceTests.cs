using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

public class TickImportServiceTests : IDisposable
{
    private readonly Mock<ITickCacheService> _cacheMock = new();
    private readonly Mock<ITickImportRepository> _repoMock = new();
    private readonly Mock<ITickDownloader> _downloaderMock = new();
    private readonly TickImportOptions _options = new() { CacheDirectory = "data/tick-cache" };
    private readonly TickImportService _sut;

    public TickImportServiceTests()
    {
        _downloaderMock.Setup(d => d.SupportedSymbols)
            .Returns(new HashSet<string> { "EURUSD", "GBPUSD", "USDJPY", "XAUUSD" });

        _sut = new TickImportService(
            _cacheMock.Object,
            _repoMock.Object,
            _downloaderMock.Object,
            Options.Create(_options),
            NullLogger<TickImportService>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task StartTickImportAsync_ValidRequest_CreatesRunningRecord()
    {
        // Arrange
        var missingDays = new List<DateTime> { new(2023, 6, 12) };
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingDays);

        _downloaderMock.Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable());

        // Act
        var importId = await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        // Assert
        Assert.NotNull(importId);
        Assert.StartsWith("tick-", importId);
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<TickImportRecord>(rec => rec.Status == TickImportStatus.Running && rec.Symbol == "EURUSD"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartTickImportAsync_StartAfterEnd_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.StartTickImportAsync(
                "EURUSD",
                new DateTimeOffset(2023, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Contains("before", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartTickImportAsync_StartEqualsEnd_ThrowsArgumentException()
    {
        var date = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.StartTickImportAsync("EURUSD", date, date));
    }

    [Fact]
    public async Task StartTickImportAsync_UnsupportedSymbol_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.StartTickImportAsync(
                "INVALID",
                new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero)));

        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartTickImportAsync_ConcurrentImport_ThrowsInvalidOperationException()
    {
        // Arrange: start first import
        var missingDays = new List<DateTime> { new(2023, 6, 12), new(2023, 6, 13) };
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingDays);

        // Make download hang so import stays active
        var tcs = new TaskCompletionSource<bool>();
        _downloaderMock.Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()))
            .Returns(HangingAsyncEnumerable(tcs));

        await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        // Act & Assert: second import should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartTickImportAsync(
                "GBPUSD",
                new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero)));

        tcs.SetResult(true); // unblock
    }

    [Fact]
    public async Task CancelImport_RunningImport_SetsCancelledStatus()
    {
        // Arrange
        var missingDays = new List<DateTime> { new(2023, 6, 12) };
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingDays);

        var downloadStarted = new TaskCompletionSource<bool>();
        _downloaderMock.Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()))
            .Returns((string s, IReadOnlyList<DateTime> d, IProgress<(int, int)>? p, CancellationToken ct) =>
                CancellableAsyncEnumerable(downloadStarted, ct));

        TickImportCompletionUpdate? completionUpdate = null;
        _sut.OnCompleted += update => completionUpdate = update;

        _repoMock.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new TickImportRecord(
                id, "Dukascopy", "EURUSD",
                new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero),
                TickImportStatus.Running, CreatedAt: DateTimeOffset.UtcNow));

        var importId = await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        await downloadStarted.Task;

        // Act
        _sut.CancelImport(importId);

        // Wait for completion event
        await Task.Delay(200);

        // Assert
        Assert.NotNull(completionUpdate);
        Assert.Equal(TickImportStatus.Cancelled, completionUpdate!.Status);
    }

    [Fact]
    public async Task StartTickImportAsync_NetworkFailure_SetsFailedStatus()
    {
        // Arrange
        var missingDays = new List<DateTime> { new(2023, 6, 12) };
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingDays);

        _downloaderMock.Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(new HttpRequestException("Connection refused")));

        _repoMock.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new TickImportRecord(
                id, "Dukascopy", "EURUSD",
                new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero),
                TickImportStatus.Running, CreatedAt: DateTimeOffset.UtcNow));

        TickImportCompletionUpdate? completionUpdate = null;
        _sut.OnCompleted += update => completionUpdate = update;

        // Act
        await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        await Task.Delay(300);

        // Assert
        Assert.NotNull(completionUpdate);
        Assert.Equal(TickImportStatus.Failed, completionUpdate!.Status);
        Assert.Contains("Connection refused", completionUpdate.ErrorMessage);
    }

    [Fact]
    public async Task StartTickImportAsync_AllDaysCached_ImmediateCompletion()
    {
        // Arrange: no missing days
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime>());
        _cacheMock.Setup(c => c.GetTickCountAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000L);

        TickImportCompletionUpdate? completionUpdate = null;
        _sut.OnCompleted += update => completionUpdate = update;

        // Act
        var importId = await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        // Assert: completed immediately with tick count
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<TickImportRecord>(rec => rec.Status == TickImportStatus.Completed && rec.TotalTickCount == 50000),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(completionUpdate);
        Assert.Equal(TickImportStatus.Completed, completionUpdate!.Status);
    }

    [Fact]
    public async Task StartTickImportAsync_Completion_RecordsTickCount()
    {
        // Arrange
        var missingDays = new List<DateTime> { new(2023, 6, 12) };
        _cacheMock.Setup(c => c.GetMissingDaysAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingDays);
        _cacheMock.Setup(c => c.GetTickCountAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(12345L);

        _downloaderMock.Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IProgress<(int, int)>?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable());

        // Act
        await _sut.StartTickImportAsync(
            "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero));

        await Task.Delay(300);

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<TickImportRecord>(rec => rec.Status == TickImportStatus.Completed && rec.TotalTickCount == 12345),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverOnStartupAsync_RunningRecords_ResetsToFailed()
    {
        // Arrange
        var runningRecord = new TickImportRecord(
            "tick-123", "Dukascopy", "EURUSD",
            new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero),
            TickImportStatus.Running,
            CreatedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TickImportRecord> { runningRecord });

        // Act
        await _sut.RecoverOnStartupAsync();

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<TickImportRecord>(rec =>
                rec.ImportId == "tick-123" &&
                rec.Status == TickImportStatus.Failed &&
                rec.ErrorDetail == "Interrupted by application restart"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helpers

    private static async IAsyncEnumerable<TickDownloadItem> EmptyAsyncEnumerable()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<TickDownloadItem> HangingAsyncEnumerable(TaskCompletionSource<bool> tcs)
    {
        await tcs.Task;
        yield break;
    }

    private static async IAsyncEnumerable<TickDownloadItem> CancellableAsyncEnumerable(
        TaskCompletionSource<bool> started, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        started.TrySetResult(true);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct);
        }
        ct.ThrowIfCancellationRequested();
        yield break;
    }

    private static async IAsyncEnumerable<TickDownloadItem> ThrowingAsyncEnumerable(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    #endregion
}
