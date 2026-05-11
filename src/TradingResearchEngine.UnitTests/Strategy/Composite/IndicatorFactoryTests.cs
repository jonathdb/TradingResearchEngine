using TradingResearchEngine.Application.Strategies.Composite;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Unit tests for the IndicatorFactory covering all 8 indicator types,
/// error handling for unknown types and missing parameters, and default parameter application.
/// </summary>
public class IndicatorFactoryTests
{
    #region Successful Creation — All 8 Types

    [Fact]
    public void Create_SmaWithValidPeriod_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("sma20", "sma", new Dictionary<string, object> { ["period"] = 20 });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("sma20", instance.Id);
        Assert.Equal("sma", instance.Type);
    }

    [Fact]
    public void Create_EmaWithValidPeriod_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("ema12", "ema", new Dictionary<string, object> { ["period"] = 12 });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("ema12", instance.Id);
        Assert.Equal("ema", instance.Type);
    }

    [Fact]
    public void Create_RsiWithValidPeriod_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("rsi14", "rsi", new Dictionary<string, object> { ["period"] = 14 });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("rsi14", instance.Id);
        Assert.Equal("rsi", instance.Type);
    }

    [Fact]
    public void Create_MacdWithValidParameters_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("macd1", "macd", new Dictionary<string, object>
        {
            ["fastPeriod"] = 12,
            ["slowPeriod"] = 26,
            ["signalPeriod"] = 9
        });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("macd1", instance.Id);
        Assert.Equal("macd", instance.Type);
    }

    [Fact]
    public void Create_BollingerWithValidParameters_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("bb20", "bollinger", new Dictionary<string, object>
        {
            ["period"] = 20,
            ["standardDeviations"] = 2.0
        });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("bb20", instance.Id);
        Assert.Equal("bollinger", instance.Type);
    }

    [Fact]
    public void Create_AtrWithValidPeriod_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("atr14", "atr", new Dictionary<string, object> { ["period"] = 14 });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("atr14", instance.Id);
        Assert.Equal("atr", instance.Type);
    }

    [Fact]
    public void Create_StochasticWithValidParameters_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("stoch1", "stochastic", new Dictionary<string, object>
        {
            ["lookbackPeriod"] = 14,
            ["signalPeriod"] = 3,
            ["smoothPeriod"] = 3
        });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("stoch1", instance.Id);
        Assert.Equal("stochastic", instance.Type);
    }

    [Fact]
    public void Create_DonchianWithValidPeriod_ReturnsNonNullInstance()
    {
        var config = new IndicatorConfig("dc20", "donchian", new Dictionary<string, object> { ["period"] = 20 });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("dc20", instance.Id);
        Assert.Equal("donchian", instance.Type);
    }

    #endregion

    #region Unknown Type

    [Fact]
    public void Create_UnknownType_ThrowsArgumentExceptionListingSupportedTypes()
    {
        var config = new IndicatorConfig("unknown1", "vwap", new Dictionary<string, object> { ["period"] = 14 });

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("vwap", ex.Message);
        Assert.Contains("sma", ex.Message);
        Assert.Contains("ema", ex.Message);
        Assert.Contains("rsi", ex.Message);
        Assert.Contains("macd", ex.Message);
        Assert.Contains("bollinger", ex.Message);
        Assert.Contains("atr", ex.Message);
        Assert.Contains("stochastic", ex.Message);
        Assert.Contains("donchian", ex.Message);
    }

    #endregion

    #region Missing Required Parameters

    [Fact]
    public void Create_SmaMissingPeriod_ThrowsArgumentExceptionIdentifyingParameter()
    {
        var config = new IndicatorConfig("sma20", "sma", new Dictionary<string, object>());

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("period", ex.Message);
        Assert.Contains("sma20", ex.Message);
    }

    [Fact]
    public void Create_EmaMissingPeriod_ThrowsArgumentExceptionIdentifyingParameter()
    {
        var config = new IndicatorConfig("ema12", "ema", null);

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("period", ex.Message);
    }

    [Fact]
    public void Create_RsiMissingPeriod_ThrowsArgumentExceptionIdentifyingParameter()
    {
        var config = new IndicatorConfig("rsi14", "rsi", new Dictionary<string, object>());

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("period", ex.Message);
    }

    [Fact]
    public void Create_AtrMissingPeriod_ThrowsArgumentExceptionIdentifyingParameter()
    {
        var config = new IndicatorConfig("atr14", "atr", new Dictionary<string, object>());

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("period", ex.Message);
    }

    [Fact]
    public void Create_DonchianMissingPeriod_ThrowsArgumentExceptionIdentifyingParameter()
    {
        var config = new IndicatorConfig("dc20", "donchian", null);

        var ex = Assert.Throws<ArgumentException>(() => IndicatorFactory.Create(config));

        Assert.Contains("period", ex.Message);
    }

    #endregion

    #region Default Parameters Applied When Omitted

    [Fact]
    public void Create_MacdWithNoParameters_AppliesDefaultsSuccessfully()
    {
        // MACD defaults: fastPeriod=12, slowPeriod=26, signalPeriod=9
        var config = new IndicatorConfig("macd1", "macd", null);

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("macd1", instance.Id);
    }

    [Fact]
    public void Create_BollingerWithNoParameters_AppliesDefaultsSuccessfully()
    {
        // Bollinger defaults: period=20, standardDeviations=2.0
        var config = new IndicatorConfig("bb1", "bollinger", null);

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("bb1", instance.Id);
    }

    [Fact]
    public void Create_StochasticWithNoParameters_AppliesDefaultsSuccessfully()
    {
        // Stochastic defaults: lookbackPeriod=14, signalPeriod=3, smoothPeriod=3
        var config = new IndicatorConfig("stoch1", "stochastic", null);

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("stoch1", instance.Id);
    }

    [Fact]
    public void Create_MacdWithPartialParameters_AppliesDefaultsForMissing()
    {
        // Only provide fastPeriod, slowPeriod and signalPeriod should default
        var config = new IndicatorConfig("macd2", "macd", new Dictionary<string, object>
        {
            ["fastPeriod"] = 8
        });

        var instance = IndicatorFactory.Create(config);

        Assert.NotNull(instance);
        Assert.Equal("macd2", instance.Id);
    }

    #endregion
}
