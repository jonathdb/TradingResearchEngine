using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.Strategy;

[StrategyName("test-strategy")]
public sealed class TestStrategy : IStrategy
{
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt) => Array.Empty<EngineEvent>();
}

[StrategyName("test-strategy-two")]
public sealed class TestStrategyTwo : IStrategy
{
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt) => Array.Empty<EngineEvent>();
}

public class StrategyRegistryTests
{
    [Fact]
    public void Resolve_RegisteredName_ReturnsCorrectType()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(TestStrategy).Assembly);

        var type = registry.Resolve("test-strategy");
        Assert.Equal(typeof(TestStrategy), type);
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsStrategyNotFoundException()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(TestStrategy).Assembly);

        var ex = Assert.Throws<StrategyNotFoundException>(() => registry.Resolve("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
        Assert.Contains("test-strategy", ex.Message); // lists known names
    }

    [Fact]
    public void KnownNames_ReflectsAllRegistered()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(TestStrategy).Assembly);

        Assert.Contains("test-strategy", registry.KnownNames);
        Assert.Contains("test-strategy-two", registry.KnownNames);
    }

    [Fact]
    public void RegisterAssembly_DuplicateName_ThrowsInvalidOperationException()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(TestStrategy).Assembly);

        // Registering the same assembly again should throw on duplicate names
        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterAssembly(typeof(TestStrategy).Assembly));
    }

    [Fact]
    public void Resolve_Composite_ReturnsCompositeStrategyType()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(CompositeStrategy).Assembly);

        var type = registry.Resolve("composite");
        Assert.Equal(typeof(CompositeStrategy), type);
    }

    [Theory]
    [InlineData("moving-average-crossover", typeof(MovingAverageCrossoverStrategy))]
    [InlineData("donchian-breakout", typeof(DonchianBreakoutStrategy))]
    [InlineData("zscore-mean-reversion", typeof(ZScoreMeanReversionStrategy))]
    [InlineData("volatility-scaled-trend", typeof(VolatilityScaledTrendStrategy))]
    [InlineData("stationary-mean-reversion", typeof(StationaryMeanReversionStrategy))]
    [InlineData("macro-regime-rotation", typeof(MacroRegimeRotationStrategy))]
    [InlineData("baseline-buy-and-hold", typeof(BaselineBuyAndHoldStrategy))]
    public void Resolve_ExistingCompiledStrategies_StillResolvable(string name, Type expectedType)
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(CompositeStrategy).Assembly);

        var type = registry.Resolve(name);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void RegisterAssembly_ApplicationAssembly_ContainsCompositeAndAllExistingStrategies()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(CompositeStrategy).Assembly);

        // Verify composite is registered alongside all existing strategies
        Assert.Contains("composite", registry.KnownNames);
        Assert.Contains("moving-average-crossover", registry.KnownNames);
        Assert.Contains("donchian-breakout", registry.KnownNames);
        Assert.Contains("zscore-mean-reversion", registry.KnownNames);
        Assert.Contains("volatility-scaled-trend", registry.KnownNames);
        Assert.Contains("stationary-mean-reversion", registry.KnownNames);
        Assert.Contains("macro-regime-rotation", registry.KnownNames);
        Assert.Contains("baseline-buy-and-hold", registry.KnownNames);
    }

    [Fact]
    public void VerifyAll_AllStrategiesValid_ReturnsAllSucceeded()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(TestStrategy).Assembly);

        var result = registry.VerifyAll();

        Assert.True(result.AllSucceeded);
        Assert.Equal(0, result.FailureCount);
        Assert.True(result.TotalRegistered > 0);
    }

    [Fact]
    public void VerifyAll_ApplicationStrategies_ReportsRegisteredCount()
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(CompositeStrategy).Assembly);

        var result = registry.VerifyAll();

        // All built-in strategies should have default-constructible parameters
        Assert.True(result.TotalRegistered >= 8); // At least the 8 known strategies
    }

    [Fact]
    public void VerifyAll_EmptyRegistry_ReturnsZeroTotalAndNoFailures()
    {
        var registry = new StrategyRegistry();

        var result = registry.VerifyAll();

        Assert.True(result.AllSucceeded);
        Assert.Equal(0, result.TotalRegistered);
        Assert.Equal(0, result.FailureCount);
    }
}