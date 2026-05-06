using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration tests for JsonGeneratedTimeframeRepository CRUD operations.
/// Validates: Requirements 10.2
/// </summary>
public class JsonGeneratedTimeframeRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonGeneratedTimeframeRepository _sut;

    public JsonGeneratedTimeframeRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gen-tf-repo-{Guid.NewGuid():N}");
        _sut = new JsonGeneratedTimeframeRepository(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveAsync_NewRecord_CanBeRetrieved()
    {
        var record = CreateRecord("gen-001", "tick-abc");

        await _sut.SaveAsync(record);
        var retrieved = await _sut.GetAsync("gen-001");

        Assert.NotNull(retrieved);
        Assert.Equal("gen-001", retrieved!.RecordId);
        Assert.Equal("tick-abc", retrieved.TickImportId);
        Assert.Equal("1H", retrieved.Timeframe);
    }

    [Fact]
    public async Task SaveAsync_UpdateExisting_OverwritesPrevious()
    {
        var record = CreateRecord("gen-002", "tick-abc");
        await _sut.SaveAsync(record);

        var updated = record with { BarCount = 9999 };
        await _sut.SaveAsync(updated);

        var retrieved = await _sut.GetAsync("gen-002");
        Assert.NotNull(retrieved);
        Assert.Equal(9999, retrieved!.BarCount);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByImportAsync_FiltersCorrectly()
    {
        await _sut.SaveAsync(CreateRecord("gen-a", "tick-1"));
        await _sut.SaveAsync(CreateRecord("gen-b", "tick-1"));
        await _sut.SaveAsync(CreateRecord("gen-c", "tick-2"));

        var forImport1 = await _sut.ListByImportAsync("tick-1");
        var forImport2 = await _sut.ListByImportAsync("tick-2");

        Assert.Equal(2, forImport1.Count);
        Assert.Single(forImport2);
    }

    [Fact]
    public async Task DeleteAsync_ExistingRecord_RemovesIt()
    {
        await _sut.SaveAsync(CreateRecord("gen-del", "tick-abc"));

        await _sut.DeleteAsync("gen-del");

        var result = await _sut.GetAsync("gen-del");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        await _sut.DeleteAsync("nonexistent"); // Should not throw
    }

    private static GeneratedTimeframeRecord CreateRecord(string recordId, string tickImportId) => new(
        RecordId: recordId,
        TickImportId: tickImportId,
        Timeframe: "1H",
        OutputFilePath: $"data/generated/dukascopy_EURUSD_1H_20230101_20240101.csv",
        OutputFileId: $"df-{recordId}",
        BarCount: 6048,
        FirstBar: new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero),
        LastBar: new DateTimeOffset(2023, 12, 29, 23, 0, 0, TimeSpan.Zero),
        GeneratedAt: DateTimeOffset.UtcNow);
}
