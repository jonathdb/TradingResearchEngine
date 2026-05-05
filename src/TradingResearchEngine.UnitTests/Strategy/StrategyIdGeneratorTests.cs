using System.Text.RegularExpressions;
using TradingResearchEngine.Application.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

/// <summary>
/// Unit tests for <see cref="StrategyIdGenerator"/> verifying URL-safe output,
/// human-readable slugs, and sufficient randomness.
/// </summary>
public class StrategyIdGeneratorTests
{
    private static readonly Regex UrlSafePattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    // ── Null/whitespace input ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Generate_NullOrWhitespace_ReturnsStrategyPrefix(string? name)
    {
        var id = StrategyIdGenerator.Generate(name);

        Assert.StartsWith("strategy-", id);
        Assert.Matches(UrlSafePattern, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_NullOrWhitespace_HasAtLeast8HexSuffix(string? name)
    {
        var id = StrategyIdGenerator.Generate(name);

        // "strategy-" is 9 chars, suffix is 8 hex chars → total at least 17
        var suffix = id["strategy-".Length..];
        Assert.Equal(8, suffix.Length);
        Assert.Matches(new Regex("^[a-f0-9]{8}$"), suffix);
    }

    // ── Normal name slugification ──

    [Fact]
    public void Generate_WithName_ProducesSlugWithSuffix()
    {
        var id = StrategyIdGenerator.Generate("Vol Trend EURUSD");

        // Should contain the slugified name portion
        Assert.Contains("vol-trend-eurusd", id);
        Assert.Matches(UrlSafePattern, id);
    }

    [Fact]
    public void Generate_WithName_HasHyphenSeparatedSuffix()
    {
        var id = StrategyIdGenerator.Generate("My Strategy");

        // Format: slug-suffix where suffix is 8 hex chars
        var lastHyphen = id.LastIndexOf('-');
        var suffix = id[(lastHyphen + 1)..];
        Assert.Equal(8, suffix.Length);
        Assert.Matches(new Regex("^[a-f0-9]{8}$"), suffix);
    }

    // ── Truncation ──

    [Fact]
    public void Generate_LongName_TruncatesSlugTo20Chars()
    {
        var longName = "This Is A Very Long Strategy Name That Exceeds Twenty Characters";
        var id = StrategyIdGenerator.Generate(longName);

        // The slug portion (before the last hyphen-suffix) should be at most 20 chars
        var lastHyphen = id.LastIndexOf('-');
        var slugPortion = id[..lastHyphen];
        Assert.True(slugPortion.Length <= 20,
            $"Slug portion '{slugPortion}' exceeds 20 characters (length: {slugPortion.Length})");
    }

    [Fact]
    public void Generate_ShortName_DoesNotPadSlug()
    {
        var id = StrategyIdGenerator.Generate("SMA");

        // "sma" is 3 chars, should not be padded
        Assert.StartsWith("sma-", id);
    }

    // ── Special characters ──

    [Fact]
    public void Generate_SpecialCharacters_RemovesNonSlugChars()
    {
        var id = StrategyIdGenerator.Generate("My $trategy! (v2.0)");

        Assert.Matches(UrlSafePattern, id);
        Assert.DoesNotContain("$", id);
        Assert.DoesNotContain("!", id);
        Assert.DoesNotContain("(", id);
        Assert.DoesNotContain(".", id);
    }

    [Fact]
    public void Generate_OnlySpecialCharacters_FallsBackToStrategyPrefix()
    {
        var id = StrategyIdGenerator.Generate("!@#$%^&*()");

        Assert.StartsWith("strategy-", id);
        Assert.Matches(UrlSafePattern, id);
    }

    // ── URL safety ──

    [Theory]
    [InlineData("Simple")]
    [InlineData("UPPER CASE")]
    [InlineData("with-hyphens-already")]
    [InlineData("numbers 123")]
    [InlineData("MiXeD CaSe With Spaces")]
    public void Generate_AnyValidName_ProducesUrlSafeId(string name)
    {
        var id = StrategyIdGenerator.Generate(name);

        Assert.Matches(UrlSafePattern, id);
    }

    // ── Uniqueness ──

    [Fact]
    public void Generate_SameName_ProducesDifferentIds()
    {
        var id1 = StrategyIdGenerator.Generate("Test Strategy");
        var id2 = StrategyIdGenerator.Generate("Test Strategy");

        Assert.NotEqual(id1, id2);
    }

    // ── Randomness ──

    [Fact]
    public void Generate_AlwaysContainsAtLeast32BitsOfRandomness()
    {
        // 8 hex chars = 32 bits of randomness
        for (var i = 0; i < 100; i++)
        {
            var id = StrategyIdGenerator.Generate("test");
            var lastHyphen = id.LastIndexOf('-');
            var suffix = id[(lastHyphen + 1)..];
            Assert.Equal(8, suffix.Length);
            Assert.Matches(new Regex("^[a-f0-9]{8}$"), suffix);
        }
    }
}
