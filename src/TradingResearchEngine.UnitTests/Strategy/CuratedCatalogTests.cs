using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

/// <summary>
/// Validates the curated strategy catalog: exactly 6 templates, all with descriptors,
/// all resolvable via StrategyRegistry, and removed types correctly throw.
/// </summary>
public class CuratedCatalogTests
{
    [Fact]
    public void DefaultTemplates_ContainExactlySix()
    {
        Assert.Equal(6, DefaultStrategyTemplates.All.Count);
    }

    [Fact]
    public void AllTemplates_HaveNonNullDescriptor()
    {
        foreach (var tpl in DefaultStrategyTemplates.All)
        {
            Assert.NotNull(tpl.Descriptor);
        }
    }

    [Theory]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("macro-regime-rotation")]
    [InlineData("baseline-buy-and-hold")]
    public void AllSixStrategyTypes_ResolveViaRegistry(string strategyType)
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(DonchianBreakoutStrategy).Assembly);

        var type = registry.Resolve(strategyType);
        Assert.NotNull(type);
    }

    [Fact]
    public void DescriptorLookup_UnknownType_ReturnsNull()
    {
        var descriptor = DefaultStrategyTemplates.All
            .FirstOrDefault(t => t.StrategyType == "nonexistent-type")?.Descriptor;

        Assert.Null(descriptor);
    }

    [Theory]
    [InlineData("sma-crossover")]
    [InlineData("bollinger-bands")]
    [InlineData("mean-reversion")]
    [InlineData("breakout")]
    [InlineData("rsi")]
    public void RemovedStrategyTypes_ThrowStrategyNotFoundException(string removedType)
    {
        var registry = new StrategyRegistry();
        registry.RegisterAssembly(typeof(DonchianBreakoutStrategy).Assembly);

        Assert.Throws<StrategyNotFoundException>(() => registry.Resolve(removedType));
    }

    [Fact]
    public void AllDescriptors_StrategyTypeMatchesTemplate()
    {
        foreach (var tpl in DefaultStrategyTemplates.All)
        {
            Assert.Equal(tpl.StrategyType, tpl.Descriptor!.StrategyType);
        }
    }

    [Fact]
    public void AllDescriptors_HaveHypothesisAndFamily()
    {
        foreach (var tpl in DefaultStrategyTemplates.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor!.Hypothesis));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor.Family));
        }
    }
}
