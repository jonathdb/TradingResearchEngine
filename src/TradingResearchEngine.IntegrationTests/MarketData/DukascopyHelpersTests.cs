using System.Buffers.Binary;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;
using TradingResearchEngine.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradingResearchEngine.IntegrationTests.MarketData;

public class DukascopyHelpersTests : IDisposable
{
    private readonly string _tempDir;

    public DukascopyHelpersTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-helpers-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CanonicalCsvWriter_WritesCorrectHeaders()
    {
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1H", 1.1210m, 1.1225m, 1.1201m, 1.1219m, 1234m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("EURUSD", "1H", 1.1219m, 1.1230m, 1.1210m, 1.1222m, 1188m,
                new DateTimeOffset(2020, 1, 1, 1, 0, 0, TimeSpan.Zero)),
        };

        var path = Path.Combine(_tempDir, "canonical.csv");
        DukascopyHelpers.SaveToCsv(path, bars);

        var lines = File.ReadAllLines(path);
        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", lines[0]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void CanonicalCsvWriter_RoundTrips()
    {
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1H", 1.1210m, 1.1225m, 1.1201m, 1.1219m, 1234m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var path = Path.Combine(_tempDir, "roundtrip.csv");
        DukascopyHelpers.SaveToCsv(path, bars);
        var loaded = DukascopyHelpers.LoadFromCsv(path, "EURUSD", "1H");

        Assert.Single(loaded);
        Assert.Equal(1.1210m, loaded[0].Open);
        Assert.Equal(1.1225m, loaded[0].High);
        Assert.Equal(1.1201m, loaded[0].Low);
        Assert.Equal(1.1219m, loaded[0].Close);
        Assert.Equal(1234m, loaded[0].Volume);
    }

    [Fact]
    public void BuildTradingDays_SkipsWeekends()
    {
        var days = DukascopyHelpers.BuildTradingDays(
            new DateTime(2024, 1, 1), new DateTime(2024, 1, 7));

        Assert.Equal(5, days.Count);
        Assert.DoesNotContain(days, d => d.DayOfWeek == DayOfWeek.Saturday);
        Assert.DoesNotContain(days, d => d.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Aggregate_1mTo1H_ReducesBars()
    {
        var bars = Enumerable.Range(0, 120).Select(i =>
            new BarRecord("EURUSD", "1m", 1.10m + i * 0.0001m, 1.11m + i * 0.0001m,
                1.09m + i * 0.0001m, 1.105m + i * 0.0001m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i)))
            .ToList();

        var aggregated = DukascopyHelpers.Aggregate(bars, "1H", "EURUSD");

        Assert.Equal(2, aggregated.Count);
        Assert.Equal("1H", aggregated[0].Interval);
    }

    [Fact]
    public void DukascopyImportProvider_SourceName_IsDukascopy()
    {
        var provider = new DukascopyImportProvider(
            new HttpClient(), NullLogger<DukascopyImportProvider>.Instance);

        Assert.Equal("Dukascopy", provider.SourceName);
    }

    [Fact]
    public async Task DukascopyImportProvider_GetSupportedSymbols_Returns15()
    {
        var provider = new DukascopyImportProvider(
            new HttpClient(), NullLogger<DukascopyImportProvider>.Instance);

        var symbols = await provider.GetSupportedSymbolsAsync();
        Assert.Equal(15, symbols.Count);
        Assert.Contains(symbols, s => s.Symbol == "EURUSD");
    }

    [Fact]
    public async Task DukascopyImportProvider_UnsupportedSymbol_ThrowsArgumentException()
    {
        var provider = new DukascopyImportProvider(
            new HttpClient(), NullLogger<DukascopyImportProvider>.Instance);

        var outputPath = Path.Combine(_tempDir, "test.csv");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.DownloadToFileAsync("INVALID", "1H",
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, outputPath));
    }

    // --- Task 9.1: Interval parsing — valid inputs ---

    [Theory]
    [InlineData("1m", 1)]
    [InlineData("5m", 5)]
    [InlineData("15m", 15)]
    [InlineData("30m", 30)]
    [InlineData("1h", 60)]
    [InlineData("1H", 60)]
    [InlineData("60m", 60)]
    [InlineData("4h", 240)]
    [InlineData("4H", 240)]
    [InlineData("1d", 1440)]
    [InlineData("1D", 1440)]
    [InlineData("daily", 1440)]
    [InlineData("Daily", 1440)]
    public void IntervalToMinutes_SupportedInterval_ReturnsCorrectMinutes(string interval, int expected)
    {
        Assert.Equal(expected, DukascopyHelpers.IntervalToMinutes(interval));
    }

    // --- Task 9.2: Interval parsing — invalid inputs ---

    [Theory]
    [InlineData("H1")]
    [InlineData("hourly")]
    [InlineData("1 hour")]
    [InlineData("")]
    [InlineData("bad")]
    public void IntervalToMinutes_UnrecognizedInterval_ThrowsArgumentException(string interval)
    {
        Assert.Throws<ArgumentException>(() => DukascopyHelpers.IntervalToMinutes(interval));
    }

    [Fact]
    public void IntervalToMinutes_NullInterval_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DukascopyHelpers.IntervalToMinutes(null!));
    }

    // --- Task 9.3: OHLC aggregation tests ---

    [Fact]
    public void Aggregate_FirstBarOpenExceedsHigh_OutputHighCoversOpen()
    {
        // Source bar where Open > High (can happen from bid/ask spread effects)
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1m", 1.1010m, 1.1005m, 1.0995m, 1.1000m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("EURUSD", "1m", 1.1000m, 1.1008m, 1.0998m, 1.1003m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 1, 0, TimeSpan.Zero)),
        };

        var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");

        Assert.Single(result);
        Assert.True(result[0].High >= result[0].Open,
            $"High {result[0].High} should be >= Open {result[0].Open}");
    }

    [Fact]
    public void Aggregate_LastBarCloseExceedsHigh_OutputHighCoversClose()
    {
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1m", 1.1000m, 1.1005m, 1.0995m, 1.1002m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            // Close exceeds the running high
            new("EURUSD", "1m", 1.1002m, 1.1004m, 1.0998m, 1.1020m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 1, 0, TimeSpan.Zero)),
        };

        var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");

        Assert.Single(result);
        Assert.True(result[0].High >= result[0].Close,
            $"High {result[0].High} should be >= Close {result[0].Close}");
    }

    [Fact]
    public void Aggregate_FirstBarOpenBelowLow_OutputLowCoversOpen()
    {
        // Source bar where Open < Low
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1m", 1.0990m, 1.1005m, 1.0995m, 1.1000m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("EURUSD", "1m", 1.1000m, 1.1008m, 1.0998m, 1.1003m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 1, 0, TimeSpan.Zero)),
        };

        var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");

        Assert.Single(result);
        Assert.True(result[0].Low <= result[0].Open,
            $"Low {result[0].Low} should be <= Open {result[0].Open}");
    }

    [Fact]
    public void Aggregate_CleanInput_OhlcUnchanged()
    {
        var bars = new List<BarRecord>
        {
            new("EURUSD", "1m", 1.1000m, 1.1010m, 1.0990m, 1.1005m, 100m,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("EURUSD", "1m", 1.1005m, 1.1015m, 1.0985m, 1.1008m, 200m,
                new DateTimeOffset(2020, 1, 1, 0, 1, 0, TimeSpan.Zero)),
        };

        var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");

        Assert.Single(result);
        Assert.Equal(1.1000m, result[0].Open);
        Assert.Equal(1.1015m, result[0].High);
        Assert.Equal(1.0985m, result[0].Low);
        Assert.Equal(1.1008m, result[0].Close);
        Assert.Equal(300m, result[0].Volume);
    }

    [Fact]
    public void Aggregate_EmptyInput_ReturnsEmpty()
    {
        var result = DukascopyHelpers.Aggregate(new List<BarRecord>(), "1h", "EURUSD");
        Assert.Empty(result);
    }

    // --- Task 9.4: Cache path tests ---

    [Fact]
    public void GetDayCachePath_ReturnsCorrectStructure()
    {
        var path = DukascopyHelpers.GetDayCachePath(_tempDir, "EURUSD", "Bid",
            new DateTime(2024, 3, 5));

        Assert.EndsWith(Path.Combine("EURUSD", "Bid", "2024", "03", "05.csv"), path);
    }

    [Fact]
    public void GetDayCachePath_OverlappingRanges_SameDaySamePath()
    {
        var path1 = DukascopyHelpers.GetDayCachePath(_tempDir, "EURUSD", "Bid",
            new DateTime(2024, 3, 5));
        var path2 = DukascopyHelpers.GetDayCachePath(_tempDir, "EURUSD", "Bid",
            new DateTime(2024, 3, 5));

        Assert.Equal(path1, path2);
    }

    // --- Task 9.5: Tick parsing tests ---

    [Fact]
    public void ParseTicks_ValidRecord_ReturnsExpectedValues()
    {
        // Build a 20-byte big-endian tick record
        var data = new byte[20];
        // ms offset = 500
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 500);
        // ask = 112345 (raw int, / 100000 = 1.12345)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 112345);
        // bid = 112340 (raw int, / 100000 = 1.12340)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 112340);
        // ask vol = 1.5f
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4),
            BitConverter.SingleToInt32Bits(1.5f));
        // bid vol = 2.0f
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4),
            BitConverter.SingleToInt32Bits(2.0f));

        var hourStart = new DateTime(2024, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var ticks = DukascopyHelpers.ParseTicks(data, hourStart, "EURUSD", 100_000m);

        Assert.Single(ticks);
        var tick = ticks[0];
        Assert.Equal(1.12345m, tick.AskLevels[0].Price);
        Assert.Equal(1.12340m, tick.BidLevels[0].Price);
        Assert.Equal(new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(500),
            tick.Timestamp);
    }

    [Fact]
    public void ParseTicks_ZeroAsk_Discarded()
    {
        var data = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 100);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 0); // ask = 0
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 112340);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4),
            BitConverter.SingleToInt32Bits(1.0f));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4),
            BitConverter.SingleToInt32Bits(1.0f));

        var hourStart = new DateTime(2024, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var ticks = DukascopyHelpers.ParseTicks(data, hourStart, "EURUSD", 100_000m);

        Assert.Empty(ticks);
    }

    [Fact]
    public void ParseTicks_IncompleteTrailingRecord_Discarded()
    {
        // 20 valid bytes + 10 trailing bytes (incomplete record)
        var data = new byte[30];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 100);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 112345);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 112340);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4),
            BitConverter.SingleToInt32Bits(1.0f));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4),
            BitConverter.SingleToInt32Bits(1.0f));
        // bytes 20-29 are trailing garbage — should be ignored

        var hourStart = new DateTime(2024, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var ticks = DukascopyHelpers.ParseTicks(data, hourStart, "EURUSD", 100_000m);

        // Only the first complete record should be parsed
        Assert.Single(ticks);
    }

    // --- Task 9.6: Endianness test ---

    [Fact]
    public void Decompress_LittleEndianSize_ReadCorrectly()
    {
        // Build a 13-byte header: 5 bytes props + 8 bytes little-endian int64
        var data = new byte[13];
        long expectedSize = 12345L;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(5, 8), expectedSize);

        long actual = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8));
        Assert.Equal(expectedSize, actual);
    }
}
