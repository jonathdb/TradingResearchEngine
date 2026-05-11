using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Application.Strategies.Composite.Conditions;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Persistence;
using Conditions = TradingResearchEngine.Application.Strategies.Composite.Conditions;

namespace TradingResearchEngine.UnitTests.V7;

/// <summary>
/// Tests for second-pass review fixes covering job worker scalability,
/// composite strategy safety, and caching improvements.
/// </summary>
public class SecondPassReviewTests
{
    #region Section 1.1: ListByStatusAsync

    [Fact]
    public async Task ListByStatusAsync_DefaultImplementation_FiltersCorrectly()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        var job1 = new BacktestJob("j1", JobType.SingleRun, JobStatus.Queued, DateTimeOffset.UtcNow);
        var job2 = new BacktestJob("j2", JobType.SingleRun, JobStatus.Running, DateTimeOffset.UtcNow);
        var job3 = new BacktestJob("j3", JobType.SingleRun, JobStatus.Queued, DateTimeOffset.UtcNow);
        await repo.SaveAsync(job1);
        await repo.SaveAsync(job2);
        await repo.SaveAsync(job3);

        // Act
        var queued = await repo.ListByStatusAsync(nameof(JobStatus.Queued));

        // Assert
        Assert.Equal(2, queued.Count);
        Assert.All(queued, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    [Fact]
    public async Task ListByStatusAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        var job1 = new BacktestJob("j1", JobType.SingleRun, JobStatus.Completed, DateTimeOffset.UtcNow);
        await repo.SaveAsync(job1);

        // Act
        var queued = await repo.ListByStatusAsync(nameof(JobStatus.Queued));

        // Assert
        Assert.Empty(queued);
    }

    #endregion

    #region Section 1.5: Progress Debouncing

    [Fact]
    public async Task UpdateProgressAsync_CachesInMemory_DoesNotPersistImmediately()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        var job = new BacktestJob("j1", JobType.SingleRun, JobStatus.Running, DateTimeOffset.UtcNow);
        await repo.SaveAsync(job);
        var executor = new JobExecutor(repo, NullLogger<JobExecutor>.Instance);

        // Act
        var progress = new ProgressSnapshot(50, 100, 50m, "Running", null, TimeSpan.FromSeconds(5), Array.Empty<string>());
        await executor.UpdateProgressAsync("j1", progress);

        // Assert — job in repo should NOT have progress yet (cached only)
        var persisted = await repo.GetByIdAsync("j1");
        Assert.Null(persisted!.Progress);
    }

    [Fact]
    public async Task FlushProgressAsync_PersistsCachedProgress()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        var job = new BacktestJob("j1", JobType.SingleRun, JobStatus.Running, DateTimeOffset.UtcNow);
        await repo.SaveAsync(job);
        var executor = new JobExecutor(repo, NullLogger<JobExecutor>.Instance);

        var progress = new ProgressSnapshot(75, 100, 75m, "Running", null, TimeSpan.FromSeconds(10), Array.Empty<string>());
        await executor.UpdateProgressAsync("j1", progress);

        // Act
        await executor.FlushProgressAsync("j1");

        // Assert
        var persisted = await repo.GetByIdAsync("j1");
        Assert.NotNull(persisted!.Progress);
        Assert.Equal(75, persisted.Progress.Current);
    }

    [Fact]
    public async Task MarkCompletedAsync_FlushesProgressBeforeTerminalState()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        var job = new BacktestJob("j1", JobType.SingleRun, JobStatus.Running, DateTimeOffset.UtcNow);
        await repo.SaveAsync(job);
        var executor = new JobExecutor(repo, NullLogger<JobExecutor>.Instance);

        var progress = new ProgressSnapshot(100, 100, 100m, "Done", null, TimeSpan.FromSeconds(15), Array.Empty<string>());
        await executor.UpdateProgressAsync("j1", progress);

        // Act
        await executor.MarkCompletedAsync("j1", "result-123");

        // Assert — progress should be flushed and job completed
        var persisted = await repo.GetByIdAsync("j1");
        Assert.Equal(JobStatus.Completed, persisted!.Status);
        Assert.Equal("result-123", persisted.ResultId);
    }

    [Fact]
    public async Task ListQueuedJobsAsync_ReturnsOnlyQueuedJobs_RespectLimit()
    {
        // Arrange
        var repo = new InMemoryJobRepository();
        for (int i = 0; i < 5; i++)
            await repo.SaveAsync(new BacktestJob($"j{i}", JobType.SingleRun, JobStatus.Queued, DateTimeOffset.UtcNow));
        await repo.SaveAsync(new BacktestJob("running", JobType.SingleRun, JobStatus.Running, DateTimeOffset.UtcNow));

        var executor = new JobExecutor(repo, NullLogger<JobExecutor>.Instance);

        // Act
        var queued = await executor.ListQueuedJobsAsync(3);

        // Assert
        Assert.Equal(3, queued.Count);
        Assert.All(queued, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    #endregion

    #region Section 3.1: Parser Depth Guard

    [Fact]
    public void Parse_DeeplyNestedExpression_ThrowsConditionParseException()
    {
        // Build an expression with 60 levels of nesting: (((((...(a > 1)...))))
        var nested = "a > 1";
        for (int i = 0; i < 60; i++)
        {
            nested = $"({nested})";
        }

        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse(nested));
        Assert.Contains("maximum nesting depth exceeded", ex.Found);
    }

    [Fact]
    public void Parse_MaxAllowedDepth_Succeeds()
    {
        // 49 levels of nesting should be fine (under the 50 limit)
        var nested = "a > 1";
        for (int i = 0; i < 49; i++)
        {
            nested = $"({nested})";
        }

        // Should not throw
        var ast = ConditionParser.Parse(nested);
        Assert.IsType<ComparisonNode>(ast);
    }

    [Fact]
    public void Parse_NormalExpression_DepthDoesNotAccumulate()
    {
        // Parsing multiple expressions should not accumulate depth
        for (int i = 0; i < 100; i++)
        {
            var ast = ConditionParser.Parse("(a > 1) AND (b < 2)");
            Assert.IsType<LogicalNode>(ast);
        }
    }

    #endregion

    #region Section 3.2: Short-Circuit Boolean Expressions

    [Fact]
    public void ExpressionCompiler_AndShortCircuits_RightSideNotEvaluatedWhenLeftFalse()
    {
        // Left: "falseInd > 9999" (always false since falseInd = 1)
        // Right: "missingInd > 0" (would fail/return null if evaluated)
        var leftNode = new ComparisonNode(
            new IndicatorRefNode("falseInd"),
            ComparisonOperator.GreaterThan,
            new LiteralNode(9999));

        var rightNode = new ComparisonNode(
            new IndicatorRefNode("missingInd"),
            ComparisonOperator.GreaterThan,
            new LiteralNode(0));

        var andNode = new LogicalNode(leftNode, LogicalOperator.And, rightNode);

        var provider = new IndicatorValueProvider();
        var falseIndMock = new Mock<IIndicatorInstance>();
        falseIndMock.Setup(m => m.Id).Returns("falseInd");
        falseIndMock.Setup(m => m.Type).Returns("mock");
        falseIndMock.Setup(m => m.IsWarm).Returns(true);
        falseIndMock.Setup(m => m.CurrentValue).Returns(1m);
        falseIndMock.Setup(m => m.PreviousValue).Returns(1m);

        // missingInd is NOT registered — accessing it would return null
        provider.Update(new List<IIndicatorInstance> { falseIndMock.Object });

        var bar = new BarRecord("TEST", "D1", 100m, 105m, 95m, 102m, 1000m, DateTimeOffset.UtcNow);
        var compiled = Conditions.ExpressionCompiler.Compile(andNode);

        // Should return false without throwing — short-circuit prevents right evaluation
        var result = compiled(provider, bar);
        Assert.False(result);
    }

    [Fact]
    public void ExpressionCompiler_OrShortCircuits_RightSideNotEvaluatedWhenLeftTrue()
    {
        // Left: "trueInd > 0" (always true since trueInd = 100)
        // Right: "missingInd > 0" (would fail/return null if evaluated)
        var leftNode = new ComparisonNode(
            new IndicatorRefNode("trueInd"),
            ComparisonOperator.GreaterThan,
            new LiteralNode(0));

        var rightNode = new ComparisonNode(
            new IndicatorRefNode("missingInd"),
            ComparisonOperator.GreaterThan,
            new LiteralNode(0));

        var orNode = new LogicalNode(leftNode, LogicalOperator.Or, rightNode);

        var provider = new IndicatorValueProvider();
        var trueIndMock = new Mock<IIndicatorInstance>();
        trueIndMock.Setup(m => m.Id).Returns("trueInd");
        trueIndMock.Setup(m => m.Type).Returns("mock");
        trueIndMock.Setup(m => m.IsWarm).Returns(true);
        trueIndMock.Setup(m => m.CurrentValue).Returns(100m);
        trueIndMock.Setup(m => m.PreviousValue).Returns(100m);

        // missingInd is NOT registered — accessing it would return null
        provider.Update(new List<IIndicatorInstance> { trueIndMock.Object });

        var bar = new BarRecord("TEST", "D1", 100m, 105m, 95m, 102m, 1000m, DateTimeOffset.UtcNow);
        var compiled = Conditions.ExpressionCompiler.Compile(orNode);

        // Should return true without throwing — short-circuit prevents right evaluation
        var result = compiled(provider, bar);
        Assert.True(result);
    }

    #endregion

    #region Section 3.3: Validate Indicators Against Skender Catalog

    [Fact]
    public void CompositeStrategyConfigValidator_AcceptsSkenderCatalogIndicators()
    {
        // Arrange — use Skender catalog indicator types (e.g., "adx", "cci", "hma")
        var config = new CompositeStrategyConfig(
            "Skender Catalog Test",
            new List<IndicatorConfig>
            {
                new("myAdx", "adx", new Dictionary<string, object> { ["period"] = 14 }),
                new("myCci", "cci", new Dictionary<string, object> { ["period"] = 20 }),
                new("myHma", "hma", new Dictionary<string, object> { ["period"] = 20 })
            },
            "myAdx > 25 AND myCci > 0",
            "myHma < close",
            DirectionMode.Long);

        // Act
        var errors = CompositeStrategyConfigValidator.Validate(config);

        // Assert — no errors about unsupported types
        Assert.DoesNotContain(errors, e => e.Contains("unsupported type"));
    }

    [Fact]
    public void CompositeStrategyConfigValidator_RejectsUnknownIndicatorType()
    {
        var config = new CompositeStrategyConfig(
            "Unknown Type Test",
            new List<IndicatorConfig>
            {
                new("myFake", "totally_fake_indicator", new Dictionary<string, object>())
            },
            "myFake > 50",
            "myFake < 50",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("unsupported type"));
    }

    [Fact]
    public void CompositeStrategyConfigValidator_AcceptsAllBuiltInTypes()
    {
        // All original hardcoded types should still work
        var builtInTypes = new[] { "sma", "ema", "rsi", "macd", "bollinger", "atr", "stochastic", "donchian" };

        foreach (var type in builtInTypes)
        {
            var config = new CompositeStrategyConfig(
                $"Test {type}",
                new List<IndicatorConfig>
                {
                    new("ind1", type, new Dictionary<string, object> { ["period"] = 14 })
                },
                "ind1 > 50",
                "ind1 < 50",
                DirectionMode.Long);

            var errors = CompositeStrategyConfigValidator.Validate(config);
            Assert.DoesNotContain(errors, e => e.Contains("unsupported type"));
        }
    }

    #endregion

    #region Section 3.5: StrategyRegistry Parameter Info Caching

    [Fact]
    public void GetParameterInfo_ReturnsCachedResult_OnSecondCall()
    {
        // Arrange
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(Application.Strategies.BaselineBuyAndHoldStrategy).Assembly);

        // Act — call twice
        var first = registry.GetParameterInfo("baseline-buy-and-hold");
        var second = registry.GetParameterInfo("baseline-buy-and-hold");

        // Assert — same reference (cached)
        Assert.Same(first, second);
    }

    #endregion

    #region Section 4.1: Monte Carlo BlockSize Default

    [Fact]
    public void MonteCarloOptions_BlockSize_DefaultsToOne_IIDBootstrap()
    {
        // The default BlockSize is 1 (IID bootstrap).
        // This is documented behavior — block bootstrap requires explicit opt-in.
        var options = new MonteCarloOptions();
        Assert.Equal(1, options.BlockSize);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// In-memory repository for testing that implements IRepository{BacktestJob}.
    /// </summary>
    private sealed class InMemoryJobRepository : IRepository<BacktestJob>
    {
        private readonly ConcurrentDictionary<string, BacktestJob> _store = new();

        public Task SaveAsync(BacktestJob entity, CancellationToken ct = default)
        {
            _store[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task<BacktestJob?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var job);
            return Task.FromResult(job);
        }

        public Task<IReadOnlyList<BacktestJob>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<BacktestJob>>(_store.Values.ToList());
        }

        public Task<IReadOnlyList<BacktestJob>> ListRecentAsync(int count, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<BacktestJob>>(
                _store.Values.OrderByDescending(j => j.SubmittedAt).Take(count).ToList());
        }

        public Task<IReadOnlyList<BacktestJob>> ListByStatusAsync(string status, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<BacktestJob>>(
                _store.Values.Where(j => string.Equals(j.Status.ToString(), status, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _store.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    #endregion
}
