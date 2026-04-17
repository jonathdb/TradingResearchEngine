using System.Globalization;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

/// <summary>
/// Preservation property tests that verify existing behavior is unchanged.
/// These tests MUST PASS on unfixed code — they encode behavior that should NOT change.
///
/// Validates: Requirements 3.1, 3.2, 3.3
/// </summary>
public class PathPreservationTests
{
    // Feature: dukascopy-file-not-found-fix, Property 2: Preservation

    /// <summary>
    /// Property P1: DataFileService explicit dataDir is respected.
    /// For all non-empty strings customDir, DataFileService(customDir).DataDirectory == customDir.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DataFileService_ExplicitDataDir_IsPreserved()
    {
        var gen = Gen.Elements("a", "b", "custom", "my-data", "test-dir", "cache", "tmp")
            .SelectMany(prefix =>
                Gen.Choose(1, 9999).Select(n => Path.Combine(Path.GetTempPath(), $"{prefix}-{n}")));

        return Prop.ForAll(
            gen.ToArbitrary(),
            customDir =>
            {
                var service = new DataFileService(customDir);
                try
                {
                    return service.DataDirectory == customDir;
                }
                finally
                {
                    // Clean up created directory
                    if (Directory.Exists(customDir))
                    {
                        try { Directory.Delete(customDir, recursive: false); } catch { }
                    }
                }
            });
    }

    /// <summary>
    /// Property P3: DukascopyHelpers cache format unchanged.
    /// For all generated BarRecord lists, SaveToCsv then LoadFromCsv round-trips
    /// produce identical OHLCV and timestamp values.
    ///
    /// Validates: Requirements 3.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DukascopyHelpers_SaveThenLoad_RoundTripsOhlcvAndTimestamp()
    {
        var barGen = from open in Gen.Choose(1, 10000).Select(n => (decimal)n / 100m)
                     from high in Gen.Choose(0, 5000).Select(n => open + (decimal)n / 100m)
                     from low in Gen.Choose(0, 5000).Select(n => open - (decimal)n / 100m)
                     from close in Gen.Choose(1, 10000).Select(n => (decimal)n / 100m)
                     from volume in Gen.Choose(0, 100000).Select(n => (decimal)n)
                     from dayOffset in Gen.Choose(0, 3650)
                     select new BarRecord(
                         "TEST", "1D", open, high, low, close, volume,
                         new DateTimeOffset(2020, 1, 1, 9, 30, 0, TimeSpan.Zero).AddDays(dayOffset));

        var barsGen = barGen.ListOf().Select(l => l.ToList());

        return Prop.ForAll(
            barsGen.ToArbitrary(),
            bars =>
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"pbt-roundtrip-{Guid.NewGuid()}.csv");
                try
                {
                    DukascopyHelpers.SaveToCsv(tempFile, bars);
                    var loaded = DukascopyHelpers.LoadFromCsv(tempFile, "TEST", "1D");

                    if (loaded.Count != bars.Count) return false;

                    for (int i = 0; i < bars.Count; i++)
                    {
                        var orig = bars[i];
                        var rt = loaded[i];
                        if (orig.Open != rt.Open) return false;
                        if (orig.High != rt.High) return false;
                        if (orig.Low != rt.Low) return false;
                        if (orig.Close != rt.Close) return false;
                        if (orig.Volume != rt.Volume) return false;
                        if (orig.Timestamp != rt.Timestamp) return false;
                    }
                    return true;
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
    }

    /// <summary>
    /// Property P2: CsvDataProvider reads valid files identically.
    /// CsvDataProvider with the fixture CSV returns expected bar count and values.
    ///
    /// Validates: Requirements 3.3
    /// </summary>
    [Fact]
    public async Task CsvDataProvider_FixtureCsv_ReturnsExpectedBarsAndValues()
    {
        // Arrange
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "bars.csv");
        var logger = new NullLoggerFactory().CreateLogger<CsvDataProvider>();
        var provider = new CsvDataProvider(fixturePath, logger);

        // Act
        var bars = new List<BarRecord>();
        await foreach (var bar in provider.GetBars("TEST", "1D",
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
        {
            bars.Add(bar);
        }

        // Assert — 4 valid bars (1 malformed row skipped)
        Assert.Equal(4, bars.Count);
        Assert.Equal(1, provider.MalformedRecordCount);

        // First bar
        Assert.Equal(100.00m, bars[0].Open);
        Assert.Equal(105.00m, bars[0].High);
        Assert.Equal(99.00m, bars[0].Low);
        Assert.Equal(103.00m, bars[0].Close);
        Assert.Equal(10000m, bars[0].Volume);
        Assert.Equal(
            DateTimeOffset.Parse("2024-01-02T09:30:00+00:00", CultureInfo.InvariantCulture),
            bars[0].Timestamp);

        // Last bar
        Assert.Equal(106.00m, bars[3].Open);
        Assert.Equal(112.00m, bars[3].High);
        Assert.Equal(105.00m, bars[3].Low);
        Assert.Equal(111.00m, bars[3].Close);
        Assert.Equal(15000m, bars[3].Volume);
        Assert.Equal(
            DateTimeOffset.Parse("2024-01-05T09:30:00+00:00", CultureInfo.InvariantCulture),
            bars[3].Timestamp);
    }
}
