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
}
