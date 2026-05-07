using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Unit tests for <see cref="DukascopyHelpers.LoadTicksFromCsv"/> malformed row handling.
/// Validates: Requirements 3.2, 3.3
/// </summary>
public class DukascopyTickCacheLoadTests
{
    private const string Header = "Timestamp,BidPrice,BidSize,AskPrice,AskSize,LastPrice,LastSize";
    private const string ValidRow = "2023-01-02T00:00:00.1230000+00:00,1.06845,1.5,1.06847,2.0,1.06846,1.5";

    [Fact]
    public void LoadTicksFromCsv_SkipsRowsWithFewerThan7Columns()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_load_{Guid.NewGuid()}.csv");
        try
        {
            var shortRow = "2023-01-02T00:00:01.000+00:00,1.06845,1.5,1.06847,2.0";
            File.WriteAllText(path, $"{Header}\n{ValidRow}\n{shortRow}\n");

            var ticks = DukascopyHelpers.LoadTicksFromCsv(path, "EURUSD");

            Assert.Single(ticks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadTicksFromCsv_SkipsRowsWithUnparseableDecimals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_load_{Guid.NewGuid()}.csv");
        try
        {
            var badDecimalRow = "2023-01-02T00:00:01.000+00:00,abc,1.5,1.06847,2.0,1.06846,1.5";
            File.WriteAllText(path, $"{Header}\n{ValidRow}\n{badDecimalRow}\n");

            var ticks = DukascopyHelpers.LoadTicksFromCsv(path, "EURUSD");

            Assert.Single(ticks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadTicksFromCsv_SkipsRowsWithUnparseableTimestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_load_{Guid.NewGuid()}.csv");
        try
        {
            var badTimestampRow = "not-a-date,1.06845,1.5,1.06847,2.0,1.06846,1.5";
            File.WriteAllText(path, $"{Header}\n{ValidRow}\n{badTimestampRow}\n");

            var ticks = DukascopyHelpers.LoadTicksFromCsv(path, "EURUSD");

            Assert.Single(ticks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadTicksFromCsv_EmptyFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_load_{Guid.NewGuid()}.csv");
        try
        {
            File.WriteAllText(path, $"{Header}\n");

            var ticks = DukascopyHelpers.LoadTicksFromCsv(path, "EURUSD");

            Assert.Empty(ticks);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
