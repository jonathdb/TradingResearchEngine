using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

public class CsvDataProviderTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "bars.csv");

    [Fact]
    public async Task GetBars_ReturnsCorrectSequence()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<CsvDataProvider>();
        var provider = new CsvDataProvider(FixturePath, logger);

        var bars = new List<Core.DataHandling.BarRecord>();
        await foreach (var bar in provider.GetBars("TEST", "1D", DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            bars.Add(bar);

        Assert.Equal(4, bars.Count);
        Assert.Equal(103.00m, bars[0].Close);
        Assert.Equal(107.00m, bars[1].Close);
        Assert.Equal(106.00m, bars[2].Close);
        Assert.Equal(111.00m, bars[3].Close);
    }

    [Fact]
    public async Task GetBars_MalformedRow_SkippedAndCounted()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<CsvDataProvider>();
        var provider = new CsvDataProvider(FixturePath, logger);

        var bars = new List<Core.DataHandling.BarRecord>();
        await foreach (var bar in provider.GetBars("TEST", "1D", DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            bars.Add(bar);

        Assert.Equal(4, bars.Count);
        Assert.Equal(1, provider.MalformedRecordCount);
    }
}
