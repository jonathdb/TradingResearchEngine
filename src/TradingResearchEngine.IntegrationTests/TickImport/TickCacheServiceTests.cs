using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.TickImport;

namespace TradingResearchEngine.IntegrationTests.TickImport;

/// <summary>
/// Integration tests for TickCacheService — write and read tick files from disk.
/// Validates: Requirements 2.1, 2.2
/// </summary>
public class TickCacheServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TickCacheService _sut;

    public TickCacheServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tick-cache-{Guid.NewGuid():N}");
        var options = Options.Create(new TickImportOptions { CacheDirectory = _tempDir });
        _sut = new TickCacheService(options, NullLogger<TickCacheService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task WriteDayTicksAsync_CreatesFileAtExpectedPath()
    {
        var date = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var ticks = CreateSampleTicks(date, 5);

        await _sut.WriteDayTicksAsync("EURUSD", date, ticks);

        var expectedPath = Path.Combine(_tempDir, "EURUSD", "ticks", "2023", "06", "15.csv");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task WriteThenRead_ProducesEquivalentTicks()
    {
        var date = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var ticks = CreateSampleTicks(date, 10);

        await _sut.WriteDayTicksAsync("EURUSD", date, ticks);

        var readTicks = new List<TickCsvRow>();
        await foreach (var tick in _sut.ReadTicksAsync("EURUSD", date, date))
        {
            readTicks.Add(tick);
        }

        Assert.Equal(ticks.Count, readTicks.Count);
        for (int i = 0; i < ticks.Count; i++)
        {
            Assert.Equal(ticks[i].Timestamp, readTicks[i].Timestamp);
            Assert.Equal(ticks[i].Bid, readTicks[i].Bid);
            Assert.Equal(ticks[i].Ask, readTicks[i].Ask);
            Assert.Equal(ticks[i].BidVolume, readTicks[i].BidVolume);
            Assert.Equal(ticks[i].AskVolume, readTicks[i].AskVolume);
        }
    }

    [Fact]
    public async Task GetMissingDaysAsync_WithCachedDays_ReturnsOnlyMissing()
    {
        // Cache Monday and Wednesday
        var monday = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var wednesday = new DateTime(2023, 6, 14, 0, 0, 0, DateTimeKind.Utc);

        await _sut.WriteDayTicksAsync("EURUSD", monday, CreateSampleTicks(monday, 1));
        await _sut.WriteDayTicksAsync("EURUSD", wednesday, CreateSampleTicks(wednesday, 1));

        // Query Mon-Fri range
        var startDate = monday;
        var endDate = new DateTime(2023, 6, 16, 0, 0, 0, DateTimeKind.Utc); // Friday

        var missing = await _sut.GetMissingDaysAsync("EURUSD", startDate, endDate);

        // Should be missing: Tuesday, Thursday, Friday
        Assert.Equal(3, missing.Count);
        Assert.Contains(new DateTime(2023, 6, 13, 0, 0, 0, DateTimeKind.Utc), missing); // Tuesday
        Assert.Contains(new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc), missing); // Thursday
        Assert.Contains(new DateTime(2023, 6, 16, 0, 0, 0, DateTimeKind.Utc), missing); // Friday
    }

    [Fact]
    public async Task GetMissingDaysAsync_SkipsWeekends()
    {
        // Query a range that includes a weekend
        var startDate = new DateTime(2023, 6, 9, 0, 0, 0, DateTimeKind.Utc); // Friday
        var endDate = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc); // Monday

        var missing = await _sut.GetMissingDaysAsync("EURUSD", startDate, endDate);

        // Should only include Friday and Monday (not Saturday/Sunday)
        Assert.Equal(2, missing.Count);
        Assert.All(missing, d => Assert.True(d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday));
    }

    [Fact]
    public async Task GetTickCountAsync_ReturnsCorrectCount()
    {
        var date = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        await _sut.WriteDayTicksAsync("EURUSD", date, CreateSampleTicks(date, 25));

        var count = await _sut.GetTickCountAsync("EURUSD", date, date);

        Assert.Equal(25, count);
    }

    [Fact]
    public async Task GetCoverageAsync_WithData_ReturnsCorrectRange()
    {
        var day1 = new DateTime(2023, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        await _sut.WriteDayTicksAsync("EURUSD", day1, CreateSampleTicks(day1, 1));
        await _sut.WriteDayTicksAsync("EURUSD", day2, CreateSampleTicks(day2, 1));

        var coverage = await _sut.GetCoverageAsync("EURUSD");

        Assert.NotNull(coverage);
        Assert.Equal(day1, coverage.Value.Earliest);
        Assert.Equal(day2, coverage.Value.Latest);
    }

    [Fact]
    public async Task GetCoverageAsync_NoData_ReturnsNull()
    {
        var coverage = await _sut.GetCoverageAsync("EURUSD");
        Assert.Null(coverage);
    }

    private static List<TickCsvRow> CreateSampleTicks(DateTime date, int count)
    {
        var ticks = new List<TickCsvRow>();
        var baseTime = new DateTimeOffset(date, TimeSpan.Zero);
        for (int i = 0; i < count; i++)
        {
            ticks.Add(new TickCsvRow(
                baseTime.AddMinutes(i),
                1.08234m + i * 0.00001m,
                1.08236m + i * 0.00001m,
                1.5m + i * 0.1m,
                2.3m + i * 0.1m));
        }
        return ticks;
    }
}
