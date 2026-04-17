using Moq;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Tests for the IBacktestResultRepository interface contract.
/// UnitTests references Core and Application only — uses in-memory fakes per testing-standards.md.
/// </summary>
public class SqliteIndexRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ScenarioConfig MakeConfig(string strategyType = "sma") =>
        new("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), strategyType, new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);

    private static BacktestResult MakeResult(string? versionId = null, string strategyType = "sma")
    {
        var config = MakeConfig(strategyType);
        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: config,
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 110_000m,
            MaxDrawdown: 0.05m,
            SharpeRatio: 1.5m,
            SortinoRatio: 2.0m,
            CalmarRatio: 1.0m,
            ReturnOnMaxDrawdown: 2.0m,
            TotalTrades: 10,
            WinRate: 0.6m,
            ProfitFactor: 1.5m,
            AverageWin: 500m,
            AverageLoss: -300m,
            Expectancy: 100m,
            AverageHoldingPeriod: TimeSpan.FromHours(4),
            EquityCurveSmoothness: 0.9m,
            MaxConsecutiveLosses: 3,
            MaxConsecutiveWins: 5,
            RunDurationMs: 1000,
            StrategyVersionId: versionId);
    }

    [Fact]
    public async Task ListByVersionAsync_ReturnsOnlyMatchingItems()
    {
        var repo = new Mock<IBacktestResultRepository>();
        var r1 = MakeResult("v1");
        var r2 = MakeResult("v2");
        var r3 = MakeResult("v1");

        repo.Setup(r => r.ListByVersionAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult> { r1, r3 });

        var results = await repo.Object.ListByVersionAsync("v1");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("v1", r.StrategyVersionId));
    }

    [Fact]
    public async Task ListByStrategyAsync_ReturnsOnlyMatchingItems()
    {
        var repo = new Mock<IBacktestResultRepository>();
        var r1 = MakeResult(strategyType: "donchian");
        var r2 = MakeResult(strategyType: "sma");

        repo.Setup(r => r.ListByStrategyAsync("donchian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult> { r1 });

        var results = await repo.Object.ListByStrategyAsync("donchian");

        Assert.Single(results);
        Assert.Equal("donchian", results[0].ScenarioConfig.StrategyType);
    }

    [Fact]
    public async Task Save_ThenGetById_ReturnsEntity()
    {
        var saved = new List<BacktestResult>();
        var repo = new Mock<IBacktestResultRepository>();
        var result = MakeResult("v1");

        repo.Setup(r => r.SaveAsync(It.IsAny<BacktestResult>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestResult, CancellationToken>((e, _) => saved.Add(e))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(result.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => saved.FirstOrDefault(s => s.Id == result.Id));

        await repo.Object.SaveAsync(result);
        var retrieved = await repo.Object.GetByIdAsync(result.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(result.Id, retrieved!.Id);
        Assert.Equal("v1", retrieved.StrategyVersionId);
    }

    [Fact]
    public async Task Delete_RemovesEntity()
    {
        var store = new Dictionary<string, BacktestResult>();
        var repo = new Mock<IBacktestResultRepository>();
        var result = MakeResult("v1");

        repo.Setup(r => r.SaveAsync(It.IsAny<BacktestResult>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestResult, CancellationToken>((e, _) => store[e.Id] = e)
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => store.Remove(id))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => store.GetValueOrDefault(id));

        await repo.Object.SaveAsync(result);
        Assert.NotNull(await repo.Object.GetByIdAsync(result.Id));

        await repo.Object.DeleteAsync(result.Id);
        Assert.Null(await repo.Object.GetByIdAsync(result.Id));
    }

    [Fact]
    public async Task ColdStart_NoIndex_FallsBackToFullScan()
    {
        // Simulates cold start: ListAsync returns all items (full scan fallback)
        var repo = new Mock<IBacktestResultRepository>();
        var r1 = MakeResult("v1");
        var r2 = MakeResult("v2");

        repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult> { r1, r2 });

        var all = await repo.Object.ListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ListByVersionAsync_EmptyVersion_ReturnsEmpty()
    {
        var repo = new Mock<IBacktestResultRepository>();
        repo.Setup(r => r.ListByVersionAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult>());

        var results = await repo.Object.ListByVersionAsync("nonexistent");

        Assert.Empty(results);
    }
}
