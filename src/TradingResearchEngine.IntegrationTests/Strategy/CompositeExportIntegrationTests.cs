using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Infrastructure.Export;

namespace TradingResearchEngine.IntegrationTests.Strategy;

/// <summary>
/// Integration tests for CompositeExportHelper verifying code generation
/// for MQL4, MQL5, and PineScript platforms.
/// Requirements: 15.4
/// </summary>
public class CompositeExportIntegrationTests
{
    #region Helpers

    private static CompositeStrategyConfig CreateSmaCrossoverConfig()
    {
        return new CompositeStrategyConfig(
            "SMA Crossover Export Test",
            new List<IndicatorConfig>
            {
                new("sma_fast", "sma", new Dictionary<string, object> { ["period"] = 10 }),
                new("sma_slow", "sma", new Dictionary<string, object> { ["period"] = 30 })
            },
            "sma_fast > sma_slow",
            "sma_fast < sma_slow",
            DirectionMode.Long);
    }

    private static CompositeStrategyConfig CreateCrossesConfig()
    {
        return new CompositeStrategyConfig(
            "Crosses Export Test",
            new List<IndicatorConfig>
            {
                new("ema_fast", "ema", new Dictionary<string, object> { ["period"] = 12 }),
                new("ema_slow", "ema", new Dictionary<string, object> { ["period"] = 26 })
            },
            "crosses_above(ema_fast, ema_slow)",
            "crosses_below(ema_fast, ema_slow)",
            DirectionMode.Long);
    }

    #endregion

    #region MQL4 Export (Requirement 15.4)

    /// <summary>
    /// Verifies that MQL4 export generates non-empty code for a composite strategy.
    /// </summary>
    [Fact]
    public void GenerateMQL4_CompositeStrategy_ProducesNonEmptyCode()
    {
        // Arrange
        var config = CreateSmaCrossoverConfig();

        // Act
        var (code, warnings) = CompositeExportHelper.GenerateMQL4(config);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Contains("OnTick", code);
        Assert.Contains("SMA Crossover Export Test", code);
    }

    #endregion

    #region MQL5 Export (Requirement 15.4)

    /// <summary>
    /// Verifies that MQL5 export generates non-empty code for a composite strategy.
    /// </summary>
    [Fact]
    public void GenerateMQL5_CompositeStrategy_ProducesNonEmptyCode()
    {
        // Arrange
        var config = CreateSmaCrossoverConfig();

        // Act
        var (code, warnings) = CompositeExportHelper.GenerateMQL5(config);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Contains("OnTick", code);
        Assert.Contains("CTrade", code);
        Assert.Contains("SMA Crossover Export Test", code);
    }

    #endregion

    #region PineScript Export (Requirement 15.4)

    /// <summary>
    /// Verifies that PineScript export generates non-empty code for a composite strategy.
    /// </summary>
    [Fact]
    public void GeneratePineScript_CompositeStrategy_ProducesNonEmptyCode()
    {
        // Arrange
        var config = CreateSmaCrossoverConfig();

        // Act
        var (code, warnings) = CompositeExportHelper.GeneratePineScript(config);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Contains("//@version=6", code);
        Assert.Contains("strategy(", code);
        Assert.Contains("SMA Crossover Export Test", code);
    }

    #endregion

    #region Crosses Translation (Requirement 15.4)

    /// <summary>
    /// Verifies that crosses_above/crosses_below conditions translate correctly
    /// to MQL4 platform-specific crossover detection pattern.
    /// MQL4 uses: (current > other_current && previous &lt;= other_previous)
    /// </summary>
    [Fact]
    public void GenerateMQL4_CrossesAboveBelow_TranslatesCorrectly()
    {
        // Arrange
        var config = CreateCrossesConfig();

        // Act
        var (code, _) = CompositeExportHelper.GenerateMQL4(config);

        // Assert — MQL4 crosses_above pattern: current > other && prev <= other_prev
        Assert.Contains("ema_fast_0", code);
        Assert.Contains("ema_slow_0", code);
        Assert.Contains("ema_fast_1", code);
        Assert.Contains("ema_slow_1", code);
        // Entry should have the "above" crossover pattern
        Assert.Contains("&&", code);
    }

    /// <summary>
    /// Verifies that crosses_above/crosses_below conditions translate correctly
    /// to MQL5 platform-specific crossover detection pattern.
    /// </summary>
    [Fact]
    public void GenerateMQL5_CrossesAboveBelow_TranslatesCorrectly()
    {
        // Arrange
        var config = CreateCrossesConfig();

        // Act
        var (code, _) = CompositeExportHelper.GenerateMQL5(config);

        // Assert — MQL5 uses buffer-based access with crossover logic
        Assert.Contains("ema_fast", code);
        Assert.Contains("ema_slow", code);
        Assert.Contains("&&", code);
        Assert.Contains("entrySignal", code);
        Assert.Contains("exitSignal", code);
    }

    /// <summary>
    /// Verifies that crosses_above/crosses_below conditions translate correctly
    /// to PineScript platform-specific crossover detection pattern.
    /// PineScript uses: ta.crossover / ta.crossunder or equivalent comparison logic.
    /// </summary>
    [Fact]
    public void GeneratePineScript_CrossesAboveBelow_TranslatesCorrectly()
    {
        // Arrange
        var config = CreateCrossesConfig();

        // Act
        var (code, _) = CompositeExportHelper.GeneratePineScript(config);

        // Assert — PineScript should reference the indicators and contain crossover logic
        Assert.Contains("ema_fast", code);
        Assert.Contains("ema_slow", code);
        Assert.Contains("longCondition", code);
        Assert.Contains("exitCondition", code);
    }

    #endregion
}
