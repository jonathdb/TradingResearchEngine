using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class SkenderIndicatorCatalogTests
{
    [Fact]
    public void All_ContainsAtLeast40Entries()
    {
        Assert.True(SkenderIndicatorCatalog.All.Count >= 40,
            $"Expected at least 40 catalog entries, got {SkenderIndicatorCatalog.All.Count}");
    }

    [Theory]
    [InlineData("macd")]
    [InlineData("adx")]
    [InlineData("stochastic")]
    [InlineData("williams")]
    [InlineData("obv")]
    [InlineData("cci")]
    [InlineData("supertrend")]
    [InlineData("keltner")]
    [InlineData("rsi")]
    [InlineData("bollinger")]
    [InlineData("atr")]
    [InlineData("ema")]
    [InlineData("sma")]
    [InlineData("donchian")]
    [InlineData("ichimoku")]
    [InlineData("psar")]
    [InlineData("stochrsi")]
    [InlineData("fisher")]
    public void Get_ReturnsEntry_ForKnownKey(string key)
    {
        var entry = SkenderIndicatorCatalog.Get(key);
        Assert.NotNull(entry);
        Assert.Equal(key, entry.Key);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownKey()
    {
        var entry = SkenderIndicatorCatalog.Get("nonexistent-indicator");
        Assert.Null(entry);
    }

    [Fact]
    public void AllEntries_HaveNonEmptyKey()
    {
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key),
                "Catalog entry has empty key.");
        }
    }

    [Fact]
    public void AllEntries_HaveNonEmptyDisplayName()
    {
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName),
                $"Catalog entry '{entry.Key}' has empty display name.");
        }
    }

    [Fact]
    public void AllEntries_HaveNonEmptyCategory()
    {
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Category),
                $"Catalog entry '{entry.Key}' has empty category.");
        }
    }

    [Fact]
    public void AllEntries_HaveNonNullInvoker()
    {
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            Assert.NotNull(entry.Invoker);
        }
    }

    [Fact]
    public void AllEntries_HaveAtLeastOnePrimaryOutputField()
    {
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.PrimaryOutputField),
                $"Catalog entry '{entry.Key}' has empty primary output field.");
            Assert.Contains(entry.PrimaryOutputField, entry.AllOutputFields);
        }
    }

    [Fact]
    public void AllEntries_HaveUniqueKeys()
    {
        var keys = SkenderIndicatorCatalog.All.Select(e => e.Key).ToList();
        var distinct = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(keys.Count, distinct.Count);
    }

    [Fact]
    public void RegisterInIndicatorRegistry_AddsEntriesToCoreRegistry()
    {
        // The catalog registration is idempotent
        SkenderIndicatorCatalog.RegisterInIndicatorRegistry();

        // After registration, the Core registry should have more than the 7 built-in entries
        Assert.True(IndicatorRegistry.All.Count > 7,
            $"Expected more than 7 entries after catalog registration, got {IndicatorRegistry.All.Count}");
    }

    [Fact]
    public void CatalogCoversAllFourCategories()
    {
        var categories = SkenderIndicatorCatalog.All
            .Select(e => e.Category)
            .Distinct()
            .ToList();

        Assert.Contains("Trend", categories);
        Assert.Contains("Momentum", categories);
        Assert.Contains("Volatility", categories);
        Assert.Contains("Volume", categories);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var lower = SkenderIndicatorCatalog.Get("macd");
        var upper = SkenderIndicatorCatalog.Get("MACD");
        var mixed = SkenderIndicatorCatalog.Get("Macd");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.NotNull(mixed);
        Assert.Equal(lower.Key, upper.Key);
        Assert.Equal(lower.Key, mixed.Key);
    }
}
