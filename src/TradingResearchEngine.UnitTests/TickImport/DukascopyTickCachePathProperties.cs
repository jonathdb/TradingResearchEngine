// Feature: tick-data-streaming-download, Property 6: Cache path determinism
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 6: For any valid cacheDir, symbol, and date, GetTickCachePath returns the same
/// string and matches the expected pattern {cacheDir}/{symbol}/ticks/{year:D4}/{month:D2}/{day:D2}.csv.
/// **Validates: Requirements 1.1, 1.3**
/// </summary>
public class DukascopyTickCachePathProperties
{
    [Property(MaxTest = 100)]
    public bool GetTickCachePath_IsDeterministic_AndMatchesExpectedPattern(
        PositiveInt yearWrap,
        PositiveInt monthWrap,
        PositiveInt dayWrap,
        PositiveInt symbolSeed)
    {
        // Generate valid date components
        var year = (yearWrap.Get % 30) + 2000; // 2000-2029
        var month = (monthWrap.Get % 12) + 1;  // 1-12
        var day = (dayWrap.Get % 28) + 1;      // 1-28 (safe for all months)
        var date = new DateTime(year, month, day);

        // Generate a non-empty symbol
        var symbols = new[] { "EURUSD", "GBPUSD", "USDJPY", "XAUUSD", "AUDUSD" };
        var symbol = symbols[symbolSeed.Get % symbols.Length];

        // Use a temp directory as cacheDir to avoid polluting the filesystem
        var cacheDir = Path.Combine(Path.GetTempPath(), $"pbt_cachepath_{Guid.NewGuid()}");

        try
        {
            // Call GetTickCachePath twice with the same inputs
            var result1 = DukascopyHelpers.GetTickCachePath(cacheDir, symbol, date);
            var result2 = DukascopyHelpers.GetTickCachePath(cacheDir, symbol, date);

            // Verify determinism: both calls return the same string
            if (result1 != result2)
                return false;

            // Verify the returned path matches the expected pattern
            var expectedPath = Path.Combine(
                cacheDir, symbol, "ticks",
                year.ToString("D4"), month.ToString("D2"), $"{day:D2}.csv");

            if (result1 != expectedPath)
                return false;

            return true;
        }
        finally
        {
            // Clean up created directories
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }
}
