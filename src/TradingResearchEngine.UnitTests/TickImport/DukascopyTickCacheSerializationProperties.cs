// Feature: tick-data-streaming-download, Property 1: Serialization round-trip fidelity
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 1: For any valid list of TickRecord objects, SaveTicksToCsv → LoadTicksFromCsv
/// produces identical count and field values.
/// **Validates: Requirements 4.1, 4.2, 3.4, 2.3, 2.4**
/// </summary>
public class DukascopyTickCacheSerializationProperties
{
    [Property(MaxTest = 100)]
    public bool RoundTrip_PreservesAllFields(
        PositiveInt tickCount,
        PositiveInt dayOffset,
        PositiveInt hourWrap,
        PositiveInt msWrap,
        PositiveInt bidPriceWrap,
        PositiveInt askPriceWrap,
        PositiveInt bidSizeWrap,
        PositiveInt askSizeWrap,
        PositiveInt lastPriceWrap,
        PositiveInt lastSizeWrap)
    {
        // Generate 1-50 ticks
        var count = (tickCount.Get % 50) + 1;
        var symbol = "EURUSD";
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ticks = new List<TickRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var timestamp = baseDate
                .AddDays((dayOffset.Get + i) % 1825)
                .AddHours((hourWrap.Get + i) % 24)
                .AddMilliseconds((msWrap.Get + i) % 60000);

            // Positive prices (0.00001 to 99.99999)
            var bidPrice = Math.Round((decimal)((bidPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);
            var askPrice = Math.Round((decimal)((askPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);
            var lastPrice = Math.Round((decimal)((lastPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);

            // Non-negative sizes (0.0 to 100.0)
            var bidSize = Math.Round((decimal)((bidSizeWrap.Get + i) % 1001) / 10m, 1);
            var askSize = Math.Round((decimal)((askSizeWrap.Get + i) % 1001) / 10m, 1);
            var lastSize = Math.Round((decimal)((lastSizeWrap.Get + i) % 1001) / 10m, 1);

            ticks.Add(new TickRecord(
                symbol,
                new[] { new BidLevel(bidPrice, bidSize) },
                new[] { new AskLevel(askPrice, askSize) },
                new LastTrade(lastPrice, lastSize, timestamp),
                timestamp));
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"pbt_roundtrip_{Guid.NewGuid()}.csv");
        try
        {
            // Serialize
            DukascopyHelpers.SaveTicksToCsv(tempFile, ticks);

            // Deserialize
            var loaded = DukascopyHelpers.LoadTicksFromCsv(tempFile, symbol);

            // Verify count
            if (loaded.Count != ticks.Count)
                return false;

            // Verify all fields match
            for (int i = 0; i < ticks.Count; i++)
            {
                var original = ticks[i];
                var restored = loaded[i];

                if (original.Timestamp != restored.Timestamp)
                    return false;
                if (original.BidLevels[0].Price != restored.BidLevels[0].Price)
                    return false;
                if (original.BidLevels[0].Size != restored.BidLevels[0].Size)
                    return false;
                if (original.AskLevels[0].Price != restored.AskLevels[0].Price)
                    return false;
                if (original.AskLevels[0].Size != restored.AskLevels[0].Size)
                    return false;
                if (original.LastTrade.Price != restored.LastTrade.Price)
                    return false;
                if (original.LastTrade.Volume != restored.LastTrade.Volume)
                    return false;
            }

            return true;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // Feature: tick-data-streaming-download, Property 2: Cache file validity after write
    /// <summary>
    /// Property 2: For any non-empty list of TickRecord objects, the file produced by
    /// SaveTicksToCsv passes IsCacheFileValid (size > 60 bytes).
    /// **Validates: Requirements 2.2, 10.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SaveTicksToCsv_ProducesValidCacheFile(
        PositiveInt tickCount,
        PositiveInt dayOffset,
        PositiveInt hourWrap,
        PositiveInt msWrap,
        PositiveInt bidPriceWrap,
        PositiveInt askPriceWrap,
        PositiveInt bidSizeWrap,
        PositiveInt askSizeWrap,
        PositiveInt lastPriceWrap,
        PositiveInt lastSizeWrap)
    {
        // Generate at least 1 valid TickRecord (1-50 ticks)
        var count = (tickCount.Get % 50) + 1;
        var symbol = "EURUSD";
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ticks = new List<TickRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var timestamp = baseDate
                .AddDays((dayOffset.Get + i) % 1825)
                .AddHours((hourWrap.Get + i) % 24)
                .AddMilliseconds((msWrap.Get + i) % 60000);

            var bidPrice = Math.Round((decimal)((bidPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);
            var askPrice = Math.Round((decimal)((askPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);
            var lastPrice = Math.Round((decimal)((lastPriceWrap.Get + i) % 9999999 + 1) / 100000m, 5);

            var bidSize = Math.Round((decimal)((bidSizeWrap.Get + i) % 1001) / 10m, 1);
            var askSize = Math.Round((decimal)((askSizeWrap.Get + i) % 1001) / 10m, 1);
            var lastSize = Math.Round((decimal)((lastSizeWrap.Get + i) % 1001) / 10m, 1);

            ticks.Add(new TickRecord(
                symbol,
                new[] { new BidLevel(bidPrice, bidSize) },
                new[] { new AskLevel(askPrice, askSize) },
                new LastTrade(lastPrice, lastSize, timestamp),
                timestamp));
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"pbt_cachevalid_{Guid.NewGuid()}.csv");
        try
        {
            // Write ticks to file
            DukascopyHelpers.SaveTicksToCsv(tempFile, ticks);

            // Verify IsCacheFileValid returns true (file size > 60 bytes)
            return DukascopyHelpers.IsCacheFileValid(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
