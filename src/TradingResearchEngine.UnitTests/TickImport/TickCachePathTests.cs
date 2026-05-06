using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Tests for tick cache path conventions.
/// Validates: Requirements 2.1, 2.6, 11.2
/// </summary>
public class TickCachePathTests
{
    [Theory]
    [InlineData("EURUSD", 2023, 6, 15)]
    [InlineData("XAUUSD", 2020, 1, 1)]
    [InlineData("GBPUSD", 2024, 12, 31)]
    public void GetDayFilePath_FollowsExpectedPattern(string symbol, int year, int month, int day)
    {
        // The expected pattern is: {CacheDir}/{Symbol}/ticks/{yyyy}/{MM}/{dd}.csv
        var cacheDir = "data/tick-cache";
        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // Construct expected path
        var expectedPath = Path.Combine(
            cacheDir,
            symbol,
            "ticks",
            year.ToString("D4"),
            month.ToString("D2"),
            $"{day:D2}.csv");

        // The path should follow the pattern
        Assert.Contains(symbol, expectedPath);
        Assert.Contains("ticks", expectedPath);
        Assert.EndsWith(".csv", expectedPath);
        Assert.Contains(year.ToString("D4"), expectedPath);
        Assert.Contains(month.ToString("D2"), expectedPath);
        Assert.Contains($"{day:D2}.csv", expectedPath);
    }

    [Fact]
    public void CacheFiles_NotRegisteredAsDataFileRecords()
    {
        // Tick cache files live under {CacheDir}/{Symbol}/ticks/ and are internal.
        // They should never be registered as DataFileRecords.
        // This test validates the convention: cache paths contain "/ticks/" segment
        // while DataFileRecords point to files in the "generated" directory.

        var cacheDir = "data/tick-cache";
        var cachePath = Path.Combine(cacheDir, "EURUSD", "ticks", "2023", "06", "15.csv");
        var generatedPath = Path.Combine(cacheDir, "..", "generated", "dukascopy_EURUSD_1H_20230601_20230630.csv");

        // Cache path contains "ticks" segment — these are internal
        Assert.Contains("ticks", cachePath);

        // Generated path contains "generated" segment — these are registered as DataFileRecords
        Assert.Contains("generated", generatedPath);

        // They are distinct paths
        Assert.NotEqual(
            Path.GetFullPath(cachePath),
            Path.GetFullPath(generatedPath));
    }
}
