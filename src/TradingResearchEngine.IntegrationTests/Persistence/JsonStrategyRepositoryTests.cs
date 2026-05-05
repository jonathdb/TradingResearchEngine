using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.Persistence;

public class JsonStrategyRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonStrategyRepository _repo;

    public JsonStrategyRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-strategy-test-{Guid.NewGuid():N}");
        _repo = new JsonStrategyRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task GetVersionCountsAsync_EmptyRepo_ReturnsZeroCounts()
    {
        var ids = new[] { "strategy-a", "strategy-b" };

        var counts = await _repo.GetVersionCountsAsync(ids);

        Assert.Equal(2, counts.Count);
        Assert.Equal(0, counts["strategy-a"]);
        Assert.Equal(0, counts["strategy-b"]);
    }

    [Fact]
    public async Task GetVersionCountsAsync_WithVersions_ReturnsCorrectCounts()
    {
        // Arrange: create two strategies with different version counts
        await _repo.SaveAsync(MakeStrategy("s1"));
        await _repo.SaveAsync(MakeStrategy("s2"));
        await _repo.SaveAsync(MakeStrategy("s3"));

        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-1", 1));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-2", 2));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-3", 3));
        await _repo.SaveVersionAsync(MakeVersion("s2", "v2-1", 1));
        // s3 has no versions

        // Act
        var counts = await _repo.GetVersionCountsAsync(new[] { "s1", "s2", "s3" });

        // Assert
        Assert.Equal(3, counts["s1"]);
        Assert.Equal(1, counts["s2"]);
        Assert.Equal(0, counts["s3"]);
    }

    [Fact]
    public async Task GetVersionCountsAsync_UnknownStrategyId_ReturnsZero()
    {
        var counts = await _repo.GetVersionCountsAsync(new[] { "nonexistent" });

        Assert.Single(counts);
        Assert.Equal(0, counts["nonexistent"]);
    }

    [Fact]
    public async Task GetVersionCountsAsync_HonoursCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _repo.GetVersionCountsAsync(new[] { "any" }, cts.Token));
    }

    [Fact]
    public async Task ListAllVersionsAsync_EmptyRepo_ReturnsEmptyList()
    {
        var versions = await _repo.ListAllVersionsAsync();

        Assert.Empty(versions);
    }

    [Fact]
    public async Task ListAllVersionsAsync_ReturnsAllVersionsAcrossStrategies()
    {
        // Arrange
        await _repo.SaveAsync(MakeStrategy("s1"));
        await _repo.SaveAsync(MakeStrategy("s2"));

        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-1", 1));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-2", 2));
        await _repo.SaveVersionAsync(MakeVersion("s2", "v2-1", 1));

        // Act
        var versions = await _repo.ListAllVersionsAsync();

        // Assert
        Assert.Equal(3, versions.Count);
    }

    [Fact]
    public async Task ListAllVersionsAsync_OrderedByStrategyIdThenVersionNumber()
    {
        // Arrange: save in non-sorted order
        await _repo.SaveAsync(MakeStrategy("s2"));
        await _repo.SaveAsync(MakeStrategy("s1"));

        await _repo.SaveVersionAsync(MakeVersion("s2", "v2-2", 2));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-1", 1));
        await _repo.SaveVersionAsync(MakeVersion("s2", "v2-1", 1));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-2", 2));

        // Act
        var versions = await _repo.ListAllVersionsAsync();

        // Assert: ordered by StrategyId then VersionNumber
        Assert.Equal(4, versions.Count);
        Assert.Equal("s1", versions[0].StrategyId);
        Assert.Equal(1, versions[0].VersionNumber);
        Assert.Equal("s1", versions[1].StrategyId);
        Assert.Equal(2, versions[1].VersionNumber);
        Assert.Equal("s2", versions[2].StrategyId);
        Assert.Equal(1, versions[2].VersionNumber);
        Assert.Equal("s2", versions[3].StrategyId);
        Assert.Equal(2, versions[3].VersionNumber);
    }

    [Fact]
    public async Task ListAllVersionsAsync_HonoursCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _repo.ListAllVersionsAsync(cts.Token));
    }

    [Fact]
    public async Task ListAllVersionsAsync_IgnoresVersionIndexDirectory()
    {
        // Arrange: create a strategy with a version (which also creates _version_index)
        await _repo.SaveAsync(MakeStrategy("s1"));
        await _repo.SaveVersionAsync(MakeVersion("s1", "v1-1", 1));

        // Act
        var versions = await _repo.ListAllVersionsAsync();

        // Assert: only the real version is returned, _version_index is skipped
        Assert.Single(versions);
        Assert.Equal("s1", versions[0].StrategyId);
    }

    // --- Helpers ---

    private static StrategyIdentity MakeStrategy(string id) =>
        new(id, $"Strategy {id}", "moving-average-crossover", DateTimeOffset.UtcNow);

    private static StrategyVersion MakeVersion(string strategyId, string versionId, int versionNumber) =>
        new(versionId, strategyId, versionNumber,
            new Dictionary<string, object> { ["Period"] = 20 },
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
                null, null, null, null),
            DateTimeOffset.UtcNow);
}
