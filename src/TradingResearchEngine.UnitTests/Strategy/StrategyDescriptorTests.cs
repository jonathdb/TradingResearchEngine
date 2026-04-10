using TradingResearchEngine.Application.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

public class StrategyDescriptorTests
{
    [Fact]
    public void AllTemplates_HaveNonNullDescriptor()
    {
        foreach (var tpl in DefaultStrategyTemplates.All)
        {
            Assert.NotNull(tpl.Descriptor);
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor!.StrategyType));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor.Family));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor.Description));
            Assert.False(string.IsNullOrWhiteSpace(tpl.Descriptor.Hypothesis));
        }
    }

    [Fact]
    public void AllTemplates_ContainExactlySix()
    {
        Assert.Equal(6, DefaultStrategyTemplates.All.Count);
    }

    [Theory]
    [InlineData("volatility-scaled-trend", StrategyFamily.Trend)]
    [InlineData("zscore-mean-reversion", StrategyFamily.MeanReversion)]
    [InlineData("donchian-breakout", StrategyFamily.Breakout)]
    [InlineData("stationary-mean-reversion", StrategyFamily.MeanReversion)]
    [InlineData("macro-regime-rotation", StrategyFamily.RegimeAware)]
    [InlineData("baseline-buy-and-hold", StrategyFamily.Benchmark)]
    public void LookupByStrategyType_ReturnsCorrectFamily(string strategyType, string expectedFamily)
    {
        var descriptor = DefaultStrategyTemplates.All
            .FirstOrDefault(t => t.StrategyType == strategyType)?.Descriptor;

        Assert.NotNull(descriptor);
        Assert.Equal(expectedFamily, descriptor!.Family);
    }

    [Fact]
    public void LookupByUnknownType_ReturnsNull()
    {
        var descriptor = DefaultStrategyTemplates.All
            .FirstOrDefault(t => t.StrategyType == "nonexistent-strategy")?.Descriptor;

        Assert.Null(descriptor);
    }

    [Fact]
    public void DescriptorStrategyType_MatchesTemplateStrategyType()
    {
        foreach (var tpl in DefaultStrategyTemplates.All)
        {
            Assert.Equal(tpl.StrategyType, tpl.Descriptor!.StrategyType);
        }
    }

    [Theory]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("macro-regime-rotation")]
    public void ActiveStrategies_HaveSuggestedStudies(string strategyType)
    {
        var descriptor = DefaultStrategyTemplates.All
            .First(t => t.StrategyType == strategyType).Descriptor!;

        Assert.NotNull(descriptor.SuggestedStudies);
        Assert.NotEmpty(descriptor.SuggestedStudies!);
    }

    [Fact]
    public void BuyAndHold_HasSuggestedStudies()
    {
        var descriptor = DefaultStrategyTemplates.All
            .First(t => t.StrategyType == "baseline-buy-and-hold").Descriptor!;

        Assert.NotNull(descriptor.SuggestedStudies);
        Assert.Contains("MonteCarlo", descriptor.SuggestedStudies!);
    }
}
