using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class IndicatorRegistryTests
{
    [Fact]
    public void All_ReturnsExactlySevenDescriptors()
    {
        Assert.Equal(7, IndicatorRegistry.All.Count);
    }

    [Theory]
    [InlineData("SMA")]
    [InlineData("EMA")]
    [InlineData("ATR")]
    [InlineData("RSI")]
    [InlineData("BollingerBands")]
    [InlineData("ZScore")]
    [InlineData("DonchianChannel")]
    public void All_ContainsDescriptorForIndicator(string name)
    {
        var descriptor = IndicatorRegistry.All.FirstOrDefault(d => d.Name == name);
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AllDescriptors_HaveNonEmptyDescription()
    {
        foreach (var descriptor in IndicatorRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description),
                $"Descriptor '{descriptor.Name}' has empty description.");
        }
    }

    [Fact]
    public void AllDescriptors_HaveNonEmptyOutputType()
    {
        foreach (var descriptor in IndicatorRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.OutputType),
                $"Descriptor '{descriptor.Name}' has empty OutputType.");
        }
    }

    [Fact]
    public void AllDescriptors_HaveAtLeastOneParameter()
    {
        foreach (var descriptor in IndicatorRegistry.All)
        {
            Assert.True(descriptor.Parameters.Count >= 1,
                $"Descriptor '{descriptor.Name}' has no parameters.");
        }
    }

    [Fact]
    public void AllParameters_HaveNonNullDefaults()
    {
        foreach (var descriptor in IndicatorRegistry.All)
        {
            foreach (var param in descriptor.Parameters)
            {
                Assert.NotNull(param.DefaultValue);
                Assert.False(string.IsNullOrWhiteSpace(param.Name),
                    $"Parameter in '{descriptor.Name}' has empty name.");
                Assert.False(string.IsNullOrWhiteSpace(param.Type),
                    $"Parameter '{param.Name}' in '{descriptor.Name}' has empty type.");
            }
        }
    }

    [Fact]
    public void BollingerBands_HasTwoParameters()
    {
        var bb = IndicatorRegistry.All.First(d => d.Name == "BollingerBands");
        Assert.Equal(2, bb.Parameters.Count);
        Assert.Equal("Period", bb.Parameters[0].Name);
        Assert.Equal("StdDevMultiplier", bb.Parameters[1].Name);
    }

    [Fact]
    public void BollingerBands_OutputType_IsBollingerBandsOutput()
    {
        var bb = IndicatorRegistry.All.First(d => d.Name == "BollingerBands");
        Assert.Equal("BollingerBandsOutput", bb.OutputType);
    }

    [Fact]
    public void DonchianChannel_OutputType_IsDonchianChannelOutput()
    {
        var dc = IndicatorRegistry.All.First(d => d.Name == "DonchianChannel");
        Assert.Equal("DonchianChannelOutput", dc.OutputType);
    }

    [Theory]
    [InlineData("SMA", "decimal")]
    [InlineData("EMA", "decimal")]
    [InlineData("ATR", "decimal")]
    [InlineData("RSI", "decimal")]
    [InlineData("ZScore", "decimal")]
    public void SingleOutputIndicators_HaveDecimalOutputType(string name, string expectedType)
    {
        var descriptor = IndicatorRegistry.All.First(d => d.Name == name);
        Assert.Equal(expectedType, descriptor.OutputType);
    }
}
