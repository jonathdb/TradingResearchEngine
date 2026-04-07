using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.Persistence;

public class JsonFileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonFileRepository<BacktestResult> _repo;

    public JsonFileRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-test-{Guid.NewGuid():N}");
        _repo = new JsonFileRepository<BacktestResult>(
            Options.Create(new RepositoryOptions { BaseDirectory = _tempDir }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndGetById_RoundTrips()
    {
        var result = MakeResult();
        await _repo.SaveAsync(result);

        var loaded = await _repo.GetByIdAsync(result.Id);
        Assert.NotNull(loaded);
        Assert.Equal(result.RunId, loaded!.RunId);
        Assert.Equal(result.EndEquity, loaded.EndEquity);
    }

    [Fact]
    public async Task GetByIdAsync_AbsentId_ReturnsNull()
    {
        var loaded = await _repo.GetByIdAsync("nonexistent");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSaved()
    {
        await _repo.SaveAsync(MakeResult());
        await _repo.SaveAsync(MakeResult());

        var all = await _repo.ListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        var result = MakeResult();
        await _repo.SaveAsync(result);
        await _repo.DeleteAsync(result.Id);

        Assert.Null(await _repo.GetByIdAsync(result.Id));
    }

    private static BacktestResult MakeResult() =>
        new(Guid.NewGuid(),
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint>(),
            new List<ClosedTrade>(),
            100_000m, 105_000m, 0.05m, 1.0m, 1.0m, null, null, 10, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);
}
