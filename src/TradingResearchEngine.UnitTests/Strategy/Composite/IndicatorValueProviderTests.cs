using Moq;
using TradingResearchEngine.Application.Strategies.Composite;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Unit tests for the IndicatorValueProvider covering value retrieval,
/// dot-notation for multi-value indicators, previous values, and warm state tracking.
/// </summary>
public class IndicatorValueProviderTests
{
    #region Update Populates Values

    [Fact]
    public void Update_WithIndicatorInstances_PopulatesValuesForRetrieval()
    {
        var provider = new IndicatorValueProvider();
        var mockIndicator = CreateWarmMockIndicator("sma20", "sma", currentValue: 105.5m, previousValue: 104.0m);

        provider.Update(new List<IIndicatorInstance> { mockIndicator.Object });

        var value = provider.GetValue("sma20");
        Assert.NotNull(value);
        Assert.Equal(105.5m, value.Value);
    }

    [Fact]
    public void Update_WithMultipleIndicators_PopulatesAllValues()
    {
        var provider = new IndicatorValueProvider();
        var sma = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);
        var rsi = CreateWarmMockIndicator("rsi14", "rsi", currentValue: 65m, previousValue: 60m);

        provider.Update(new List<IIndicatorInstance> { sma.Object, rsi.Object });

        Assert.Equal(100m, provider.GetValue("sma20"));
        Assert.Equal(65m, provider.GetValue("rsi14"));
    }

    [Fact]
    public void Update_ReplacesExistingValues_WithNewOnes()
    {
        var provider = new IndicatorValueProvider();
        var first = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);
        provider.Update(new List<IIndicatorInstance> { first.Object });

        var second = CreateWarmMockIndicator("sma20", "sma", currentValue: 110m, previousValue: 100m);
        provider.Update(new List<IIndicatorInstance> { second.Object });

        Assert.Equal(110m, provider.GetValue("sma20"));
    }

    #endregion

    #region GetValue with Dot Notation

    [Fact]
    public void GetValue_DotNotation_ReturnsSubPropertyValue()
    {
        var provider = new IndicatorValueProvider();
        var macd = CreateWarmMockIndicatorWithSubValues("macd1", "macd",
            currentValue: 1.5m,
            previousValue: 1.2m,
            subValues: new Dictionary<string, decimal?> { ["Signal"] = 0.8m, ["Histogram"] = 0.7m });

        provider.Update(new List<IIndicatorInstance> { macd.Object });

        Assert.Equal(0.8m, provider.GetValue("macd1.Signal"));
        Assert.Equal(0.7m, provider.GetValue("macd1.Histogram"));
    }

    [Fact]
    public void GetValue_DotNotationWithoutSubProperty_ReturnsPrimaryValue()
    {
        var provider = new IndicatorValueProvider();
        var macd = CreateWarmMockIndicatorWithSubValues("macd1", "macd",
            currentValue: 1.5m,
            previousValue: 1.2m,
            subValues: new Dictionary<string, decimal?> { ["Signal"] = 0.8m });

        provider.Update(new List<IIndicatorInstance> { macd.Object });

        Assert.Equal(1.5m, provider.GetValue("macd1"));
    }

    [Fact]
    public void GetValue_UnknownIndicatorId_ReturnsNull()
    {
        var provider = new IndicatorValueProvider();
        var sma = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);
        provider.Update(new List<IIndicatorInstance> { sma.Object });

        Assert.Null(provider.GetValue("unknown"));
    }

    #endregion

    #region GetPreviousValue

    [Fact]
    public void GetPreviousValue_WarmIndicator_ReturnsPriorBarValue()
    {
        var provider = new IndicatorValueProvider();
        var sma = CreateWarmMockIndicator("sma20", "sma", currentValue: 105m, previousValue: 103m);

        provider.Update(new List<IIndicatorInstance> { sma.Object });

        var previousValue = provider.GetPreviousValue("sma20");
        Assert.NotNull(previousValue);
        Assert.Equal(103m, previousValue.Value);
    }

    [Fact]
    public void GetPreviousValue_DotNotation_ReturnsPreviousSubPropertyValue()
    {
        var provider = new IndicatorValueProvider();
        var macd = CreateWarmMockIndicatorWithPreviousSubValues("macd1", "macd",
            currentValue: 1.5m,
            previousValue: 1.2m,
            subValues: new Dictionary<string, decimal?> { ["Signal"] = 0.8m },
            previousSubValues: new Dictionary<string, decimal?> { ["Signal"] = 0.6m });

        provider.Update(new List<IIndicatorInstance> { macd.Object });

        Assert.Equal(0.6m, provider.GetPreviousValue("macd1.Signal"));
    }

    [Fact]
    public void GetPreviousValue_UnknownIndicator_ReturnsNull()
    {
        var provider = new IndicatorValueProvider();
        provider.Update(new List<IIndicatorInstance>());

        Assert.Null(provider.GetPreviousValue("nonexistent"));
    }

    #endregion

    #region AllWarm — False When Any Indicator Not Warm

    [Fact]
    public void AllWarm_AnyIndicatorNotWarm_ReturnsFalse()
    {
        var provider = new IndicatorValueProvider();
        var warm = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);
        var cold = CreateColdMockIndicator("ema50", "ema");

        provider.Update(new List<IIndicatorInstance> { warm.Object, cold.Object });

        Assert.False(provider.AllWarm);
    }

    [Fact]
    public void AllWarm_AllIndicatorsNotWarm_ReturnsFalse()
    {
        var provider = new IndicatorValueProvider();
        var cold1 = CreateColdMockIndicator("sma20", "sma");
        var cold2 = CreateColdMockIndicator("ema50", "ema");

        provider.Update(new List<IIndicatorInstance> { cold1.Object, cold2.Object });

        Assert.False(provider.AllWarm);
    }

    #endregion

    #region AllWarm — True When All Indicators Warm

    [Fact]
    public void AllWarm_AllIndicatorsWarm_ReturnsTrue()
    {
        var provider = new IndicatorValueProvider();
        var warm1 = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);
        var warm2 = CreateWarmMockIndicator("rsi14", "rsi", currentValue: 65m, previousValue: 60m);

        provider.Update(new List<IIndicatorInstance> { warm1.Object, warm2.Object });

        Assert.True(provider.AllWarm);
    }

    [Fact]
    public void AllWarm_SingleWarmIndicator_ReturnsTrue()
    {
        var provider = new IndicatorValueProvider();
        var warm = CreateWarmMockIndicator("sma20", "sma", currentValue: 100m, previousValue: 99m);

        provider.Update(new List<IIndicatorInstance> { warm.Object });

        Assert.True(provider.AllWarm);
    }

    #endregion

    #region Unavailable (Not Warm) Indicator Returns Null

    [Fact]
    public void GetValue_IndicatorNotWarm_ReturnsNull()
    {
        var provider = new IndicatorValueProvider();
        var cold = CreateColdMockIndicator("sma20", "sma");

        provider.Update(new List<IIndicatorInstance> { cold.Object });

        var value = provider.GetValue("sma20");
        Assert.Null(value);
    }

    [Fact]
    public void GetPreviousValue_IndicatorNotWarm_ReturnsNull()
    {
        var provider = new IndicatorValueProvider();
        var cold = CreateColdMockIndicator("sma20", "sma");

        provider.Update(new List<IIndicatorInstance> { cold.Object });

        var value = provider.GetPreviousValue("sma20");
        Assert.Null(value);
    }

    [Fact]
    public void GetValue_DotNotation_IndicatorNotWarm_ReturnsNull()
    {
        var provider = new IndicatorValueProvider();
        var cold = CreateColdMockIndicator("macd1", "macd");

        provider.Update(new List<IIndicatorInstance> { cold.Object });

        var value = provider.GetValue("macd1.Signal");
        Assert.Null(value);
    }

    #endregion

    #region Test Helpers

    private static Mock<IIndicatorInstance> CreateWarmMockIndicator(
        string id, string type, decimal? currentValue, decimal? previousValue)
    {
        var mock = new Mock<IIndicatorInstance>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.Type).Returns(type);
        mock.Setup(m => m.IsWarm).Returns(true);
        mock.Setup(m => m.CurrentValue).Returns(currentValue);
        mock.Setup(m => m.PreviousValue).Returns(previousValue);
        mock.Setup(m => m.GetSubValue(It.IsAny<string>())).Returns((decimal?)null);
        mock.Setup(m => m.GetPreviousSubValue(It.IsAny<string>())).Returns((decimal?)null);
        return mock;
    }

    private static Mock<IIndicatorInstance> CreateColdMockIndicator(string id, string type)
    {
        var mock = new Mock<IIndicatorInstance>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.Type).Returns(type);
        mock.Setup(m => m.IsWarm).Returns(false);
        mock.Setup(m => m.CurrentValue).Returns((decimal?)null);
        mock.Setup(m => m.PreviousValue).Returns((decimal?)null);
        mock.Setup(m => m.GetSubValue(It.IsAny<string>())).Returns((decimal?)null);
        mock.Setup(m => m.GetPreviousSubValue(It.IsAny<string>())).Returns((decimal?)null);
        return mock;
    }

    private static Mock<IIndicatorInstance> CreateWarmMockIndicatorWithSubValues(
        string id, string type, decimal? currentValue, decimal? previousValue,
        Dictionary<string, decimal?> subValues)
    {
        var mock = CreateWarmMockIndicator(id, type, currentValue, previousValue);
        mock.Setup(m => m.GetSubValue(It.IsAny<string>()))
            .Returns<string>(sub => subValues.GetValueOrDefault(sub));
        return mock;
    }

    private static Mock<IIndicatorInstance> CreateWarmMockIndicatorWithPreviousSubValues(
        string id, string type, decimal? currentValue, decimal? previousValue,
        Dictionary<string, decimal?> subValues,
        Dictionary<string, decimal?> previousSubValues)
    {
        var mock = CreateWarmMockIndicator(id, type, currentValue, previousValue);
        mock.Setup(m => m.GetSubValue(It.IsAny<string>()))
            .Returns<string>(sub => subValues.GetValueOrDefault(sub));
        mock.Setup(m => m.GetPreviousSubValue(It.IsAny<string>()))
            .Returns<string>(sub => previousSubValues.GetValueOrDefault(sub));
        return mock;
    }

    #endregion
}
