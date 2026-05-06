using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration tests for JsonTickImportRepository CRUD operations.
/// Validates: Requirements 9.2
/// </summary>
public class JsonTickImportRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonTickImportRepository _sut;

    public JsonTickImportRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tick-import-repo-{Guid.NewGuid():N}");
        _sut = new JsonTickImportRepository(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveAsync_NewRecord_CanBeRetrieved()
    {
        var record = CreateRecord("tick-001");

        await _sut.SaveAsync(record);
        var retrieved = await _sut.GetAsync("tick-001");

        Assert.NotNull(retrieved);
        Assert.Equal("tick-001", retrieved!.ImportId);
        Assert.Equal("EURUSD", retrieved.Symbol);
        Assert.Equal(TickImportStatus.Running, retrieved.Status);
    }

    [Fact]
    public async Task SaveAsync_UpdateExisting_OverwritesPrevious()
    {
        var record = CreateRecord("tick-002");
        await _sut.SaveAsync(record);

        var updated = record with { Status = TickImportStatus.Completed, TotalTickCount = 5000 };
        await _sut.SaveAsync(updated);

        var retrieved = await _sut.GetAsync("tick-002");
        Assert.NotNull(retrieved);
        Assert.Equal(TickImportStatus.Completed, retrieved!.Status);
        Assert.Equal(5000, retrieved.TotalTickCount);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_MultipleRecords_ReturnsAll()
    {
        await _sut.SaveAsync(CreateRecord("tick-a"));
        await _sut.SaveAsync(CreateRecord("tick-b"));
        await _sut.SaveAsync(CreateRecord("tick-c"));

        var all = await _sut.ListAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task DeleteAsync_ExistingRecord_RemovesIt()
    {
        await _sut.SaveAsync(CreateRecord("tick-del"));

        await _sut.DeleteAsync("tick-del");

        var result = await _sut.GetAsync("tick-del");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        await _sut.DeleteAsync("nonexistent"); // Should not throw
    }

    private static TickImportRecord CreateRecord(string importId) => new(
        ImportId: importId,
        Source: "Dukascopy",
        Symbol: "EURUSD",
        RequestedStart: new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
        RequestedEnd: new DateTimeOffset(2023, 6, 30, 0, 0, 0, TimeSpan.Zero),
        Status: TickImportStatus.Running,
        CreatedAt: DateTimeOffset.UtcNow);
}
