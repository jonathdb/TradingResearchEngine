using TradingResearchEngine.Application.MarketData;
using TradingResearchEngine.Infrastructure.MarketData;

namespace TradingResearchEngine.IntegrationTests.Persistence;

public class JsonMarketDataImportRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonMarketDataImportRepository _repo;

    public JsonMarketDataImportRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-import-test-{Guid.NewGuid():N}");
        _repo = new JsonMarketDataImportRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips()
    {
        var record = MakeRecord("imp-001", MarketDataImportStatus.Running);
        await _repo.SaveAsync(record);

        var loaded = await _repo.GetAsync("imp-001");
        Assert.NotNull(loaded);
        Assert.Equal("imp-001", loaded!.ImportId);
        Assert.Equal("Dukascopy", loaded.Source);
        Assert.Equal("EURUSD", loaded.Symbol);
        Assert.Equal("1H", loaded.Timeframe);
        Assert.Equal(MarketDataImportStatus.Running, loaded.Status);
        Assert.Equal("Bid", loaded.CandleBasis);
    }

    [Fact]
    public async Task GetAsync_AbsentId_ReturnsNull()
    {
        var loaded = await _repo.GetAsync("nonexistent");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSaved()
    {
        await _repo.SaveAsync(MakeRecord("imp-001", MarketDataImportStatus.Completed));
        await _repo.SaveAsync(MakeRecord("imp-002", MarketDataImportStatus.Failed));

        var all = await _repo.ListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingRecord()
    {
        var record = MakeRecord("imp-001", MarketDataImportStatus.Running);
        await _repo.SaveAsync(record);

        var updated = record with
        {
            Status = MarketDataImportStatus.Completed,
            OutputFilePath = "datafiles/test.csv",
            CompletedAt = DateTimeOffset.UtcNow
        };
        await _repo.SaveAsync(updated);

        var loaded = await _repo.GetAsync("imp-001");
        Assert.Equal(MarketDataImportStatus.Completed, loaded!.Status);
        Assert.Equal("datafiles/test.csv", loaded.OutputFilePath);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        await _repo.SaveAsync(MakeRecord("imp-001", MarketDataImportStatus.Completed));
        await _repo.DeleteAsync("imp-001");

        Assert.Null(await _repo.GetAsync("imp-001"));
    }

    [Fact]
    public async Task DeleteAsync_AbsentId_DoesNotThrow()
    {
        await _repo.DeleteAsync("nonexistent");
    }

    private static MarketDataImportRecord MakeRecord(string id, MarketDataImportStatus status) =>
        new(ImportId: id,
            Source: "Dukascopy",
            Symbol: "EURUSD",
            Timeframe: "1H",
            RequestedStart: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Status: status,
            CreatedAt: DateTimeOffset.UtcNow);
}
