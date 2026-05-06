// Feature: tick-data-first-import, Property 1: Tick CSV Serialization Round-Trip
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 1: For any valid TickCsvRow (positive bid/ask, non-negative volumes, valid timestamp),
/// serialize then deserialize produces equivalent row.
/// **Validates: Requirements 6.1, 2.2, 2.3, 2.4**
/// </summary>
public class TickCsvSerializationProperties
{
    [Property(MaxTest = 100)]
    public bool SerializeThenDeserialize_ProducesEquivalentRow(PositiveInt dayOffset, PositiveInt hourWrap, PositiveInt msWrap,
        PositiveInt bidWrap, PositiveInt askWrap, PositiveInt bidVolWrap, PositiveInt askVolWrap)
    {
        // Generate valid timestamp
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timestamp = baseDate
            .AddDays(dayOffset.Get % 1825) // up to 5 years
            .AddHours(hourWrap.Get % 24)
            .AddMilliseconds(msWrap.Get % 60000);

        // Generate valid prices (0.00001 to 999999)
        var bid = Math.Round((decimal)(bidWrap.Get % 999999) + 0.00001m, 5);
        var ask = Math.Round((decimal)(askWrap.Get % 999999) + 0.00001m, 5);

        // Generate valid volumes (0 to 10000)
        var bidVolume = Math.Round((decimal)(bidVolWrap.Get % 10001), 1);
        var askVolume = Math.Round((decimal)(askVolWrap.Get % 10001), 1);

        var original = new TickCsvRow(timestamp, bid, ask, bidVolume, askVolume);

        // Serialize then deserialize
        var csv = TickCsvSerializer.Serialize(original);
        var deserialized = TickCsvSerializer.Deserialize(csv);

        if (deserialized is null) return false;

        var result = deserialized.Value;

        return result.Timestamp == original.Timestamp
            && result.Bid == original.Bid
            && result.Ask == original.Ask
            && result.BidVolume == original.BidVolume
            && result.AskVolume == original.AskVolume;
    }
}
