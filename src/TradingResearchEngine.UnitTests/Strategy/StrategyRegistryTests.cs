using TradingResearchEngine.Application.Strategy;
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
}
