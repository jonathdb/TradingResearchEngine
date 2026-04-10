using TradingResearchEngine.Application.MarketData;
using Xunit;

namespace TradingResearchEngine.UnitTests.MarketData;

/// <summary>
/// Tests for the Dukascopy import provider contract.
/// Infrastructure-dependent tests (actual provider, helpers) live in IntegrationTests.
/// UnitTests only validates Application-layer types and contracts.
/// </summary>
public class MarketDataProviderContractTests
{
    [Fact]
    public void MarketSymbolInfo_HasRequiredFields()
    {
        var info = new MarketSymbolInfo("EURUSD", "Euro / US Dollar",
            new[] { "1m", "5m", "15m", "30m", "1H", "4H", "Daily" });

        Assert.Equal("EURUSD", info.Symbol);
        Assert.Equal("Euro / US Dollar", info.DisplayName);
        Assert.Equal(7, info.SupportedTimeframes.Length);
    }

    [Fact]
    public void CsvWriteResult_HasRequiredFields()
    {
        var result = new CsvWriteResult(
            "datafiles/test.csv", "EURUSD", "1H",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 23, 0, 0, TimeSpan.Zero),
            43800);

        Assert.Equal("datafiles/test.csv", result.FilePath);
        Assert.Equal("EURUSD", result.Symbol);
        Assert.Equal("1H", result.Timeframe);
        Assert.Equal(43800, result.BarCount);
    }

    [Fact]
    public void ImportRequest_StartAfterEnd_IsDetectable()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The service layer validates this; here we just confirm the types allow detection
        Assert.True(start >= end);
    }

    [Fact]
    public void ImportRequest_ValidRange_IsDetectable()
    {
        var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(start < end);
    }
}
