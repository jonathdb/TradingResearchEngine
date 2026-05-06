using System.Globalization;

namespace TradingResearchEngine.Application.TickImport;

/// <summary>Static helpers for tick CSV serialization/deserialization.</summary>
public static class TickCsvSerializer
{
    /// <summary>CSV header line for tick data files.</summary>
    public const string Header = "Timestamp,Bid,Ask,BidVolume,AskVolume";

    /// <summary>Serializes a single tick row to a CSV line.</summary>
    public static string Serialize(TickCsvRow tick)
        => string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4}",
            tick.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            tick.Bid, tick.Ask, tick.BidVolume, tick.AskVolume);

    /// <summary>Deserializes a CSV line back to a TickCsvRow, or null if malformed.</summary>
    public static TickCsvRow? Deserialize(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 5) return null;
        try
        {
            return new TickCsvRow(
                DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                decimal.Parse(parts[1], CultureInfo.InvariantCulture),
                decimal.Parse(parts[2], CultureInfo.InvariantCulture),
                decimal.Parse(parts[3], CultureInfo.InvariantCulture),
                decimal.Parse(parts[4], CultureInfo.InvariantCulture));
        }
        catch { return null; }
    }
}
