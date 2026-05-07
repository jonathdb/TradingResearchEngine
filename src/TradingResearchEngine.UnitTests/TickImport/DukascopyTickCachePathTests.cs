using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Unit tests for <see cref="DukascopyHelpers.GetTickCachePath"/> format validation.
/// Validates: Requirements 1.1, 1.2
/// </summary>
public class DukascopyTickCachePathTests
{
    [Theory]
    [InlineData(2023, 6, 15)]
    [InlineData(2020, 1, 1)]
    [InlineData(2024, 12, 31)]
    public void GetTickCachePath_ReturnsCorrectFormat_ForVariousDates(int year, int month, int day)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test_cachepath_{Guid.NewGuid()}");
        var symbol = "EURUSD";
        var date = new DateTime(year, month, day);

        try
        {
            var result = DukascopyHelpers.GetTickCachePath(cacheDir, symbol, date);

            var expectedPath = Path.Combine(
                cacheDir, symbol, "ticks",
                year.ToString("D4"), month.ToString("D2"), $"{day:D2}.csv");

            Assert.Equal(expectedPath, result);
            Assert.Contains($"{year:D4}", result);
            Assert.Contains($"{month:D2}", result);
            Assert.Contains($"{day:D2}.csv", result);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void GetTickCachePath_CreatesDirectoryStructure()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test_cachepath_{Guid.NewGuid()}");
        var symbol = "GBPUSD";
        var date = new DateTime(2023, 3, 10);

        try
        {
            var result = DukascopyHelpers.GetTickCachePath(cacheDir, symbol, date);

            var directory = Path.GetDirectoryName(result)!;
            Assert.True(Directory.Exists(directory),
                $"Expected directory to exist: {directory}");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void GetTickCachePath_ContainsTicksSegment()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test_cachepath_{Guid.NewGuid()}");
        var symbol = "USDJPY";
        var date = new DateTime(2024, 7, 22);

        try
        {
            var result = DukascopyHelpers.GetTickCachePath(cacheDir, symbol, date);

            // The path should contain "ticks" as a path segment, not a price type like "Bid"
            Assert.Contains(Path.Combine("ticks"), result);
            Assert.DoesNotContain("Bid", result);
            Assert.DoesNotContain("Ask", result);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }
}
