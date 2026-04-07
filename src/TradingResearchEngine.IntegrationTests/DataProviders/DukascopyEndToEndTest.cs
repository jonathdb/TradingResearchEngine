using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

public class DukascopyEndToEndTest
{
    [Fact]
    public async Task DailyBars_FromDukascopy_ReturnNonZeroCount()
    {
        var http = new HttpClient();
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();
        var provider = new DukascopyDataProvider(http, logger);

        var from = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 3, 31, 23, 59, 59, TimeSpan.Zero);

        var bars = new List<BarRecord>();
        await foreach (var bar in provider.GetBars("EURUSD", "1d", from, to))
        {
            bars.Add(bar);
            Console.WriteLine($"Bar: {bar.Timestamp:yyyy-MM-dd} O={bar.Open:F5} H={bar.High:F5} L={bar.Low:F5} C={bar.Close:F5} V={bar.Volume}");
        }

        Console.WriteLine($"Total daily bars: {bars.Count}");
        Assert.True(bars.Count > 10, $"Expected >10 daily bars for March 2024, got {bars.Count}");
    }
}
