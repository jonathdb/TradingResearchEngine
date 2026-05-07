// Feature: tick-data-streaming-download, Property 5: Malformed row resilience
using System.Globalization;
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 5: For any CSV with a mix of valid and malformed rows,
/// LoadTicksFromCsv returns exactly the valid rows without throwing.
/// **Validates: Requirements 3.2, 3.3, 10.2**
/// </summary>
public class DukascopyTickCacheResilienceProperties
{
    [Property(MaxTest = 100)]
    public bool LoadTicksFromCsv_ReturnsOnlyValidRows_WithoutThrowing(
        PositiveInt validCountWrap,
        PositiveInt malformedCountWrap,
        PositiveInt seedWrap,
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
        // Generate 3-20 valid rows and 1-10 malformed rows
        var validCount = (validCountWrap.Get % 18) + 3;
        var malformedCount = (malformedCountWrap.Get % 10) + 1;
        var symbol = "EURUSD";
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Build valid CSV rows
        var validRows = new List<string>(validCount);
        for (int i = 0; i < validCount; i++)
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

            var row = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O},{1},{2},{3},{4},{5},{6}",
                timestamp, bidPrice, bidSize, askPrice, askSize, lastPrice, lastSize);

            validRows.Add(row);
        }

        // Build malformed CSV rows (cycle through 3 types)
        var malformedRows = new List<string>(malformedCount);
        for (int i = 0; i < malformedCount; i++)
        {
            var malformedType = i % 3;
            var row = malformedType switch
            {
                0 => "1.0,2.0,3.0",                                    // fewer than 7 columns
                1 => "not-a-date,abc,def,ghi,jkl,mno,pqr",            // unparseable values
                2 => "",                                                // empty row
                _ => ""
            };
            malformedRows.Add(row);
        }

        // Interleave valid and malformed rows using a deterministic shuffle
        var allRows = new List<string>(validCount + malformedCount);
        int vi = 0, mi = 0;
        var rng = new Random(seedWrap.Get);
        while (vi < validRows.Count || mi < malformedRows.Count)
        {
            if (vi < validRows.Count && mi < malformedRows.Count)
            {
                if (rng.Next(2) == 0)
                    allRows.Add(validRows[vi++]);
                else
                    allRows.Add(malformedRows[mi++]);
            }
            else if (vi < validRows.Count)
            {
                allRows.Add(validRows[vi++]);
            }
            else
            {
                allRows.Add(malformedRows[mi++]);
            }
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"pbt_resilience_{Guid.NewGuid()}.csv");
        try
        {
            // Write CSV with header and interleaved rows
            using (var writer = new StreamWriter(tempFile))
            {
                writer.WriteLine("Timestamp,BidPrice,BidSize,AskPrice,AskSize,LastPrice,LastSize");
                foreach (var row in allRows)
                {
                    writer.WriteLine(row);
                }
            }

            // Call LoadTicksFromCsv — must not throw
            var loaded = DukascopyHelpers.LoadTicksFromCsv(tempFile, symbol);

            // Verify: count of returned ticks equals count of valid rows written
            if (loaded.Count != validCount)
                return false;

            // Verify: all returned ticks have valid data (positive prices, non-negative sizes)
            foreach (var tick in loaded)
            {
                if (tick.BidLevels[0].Price <= 0) return false;
                if (tick.BidLevels[0].Size < 0) return false;
                if (tick.AskLevels[0].Price <= 0) return false;
                if (tick.AskLevels[0].Size < 0) return false;
                if (tick.LastTrade.Price <= 0) return false;
                if (tick.LastTrade.Volume < 0) return false;
                if (tick.Timestamp == default) return false;
            }

            return true;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
