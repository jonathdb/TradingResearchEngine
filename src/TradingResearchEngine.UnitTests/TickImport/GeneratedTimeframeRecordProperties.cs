// Feature: tick-data-first-import, Property 9: GeneratedTimeframeRecord JSON Round-Trip
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 9: For any valid GeneratedTimeframeRecord, serialize to JSON then deserialize produces equivalent record.
/// **Validates: Requirements 10.3**
/// </summary>
public class GeneratedTimeframeRecordProperties
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] Timeframes = { "1m", "5m", "15m", "30m", "1H", "4H", "Daily" };
    private static readonly string[] Symbols = { "EURUSD", "GBPUSD", "USDJPY", "XAUUSD" };

    [Property(MaxTest = 100)]
    public bool JsonRoundTrip_ProducesEquivalentRecord(
        PositiveInt tfIdx, PositiveInt symbolIdx, PositiveInt barCount, PositiveInt dayOffset)
    {
        var timeframe = Timeframes[tfIdx.Get % Timeframes.Length];
        var symbol = Symbols[symbolIdx.Get % Symbols.Length];
        var bars = (barCount.Get % 10000) + 1;
        var firstBar = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset.Get % 365);
        var lastBar = firstBar.AddDays(bars / 24); // approximate

        var record = new GeneratedTimeframeRecord(
            RecordId: $"gen-{Guid.NewGuid():N}",
            TickImportId: $"tick-{Guid.NewGuid():N}",
            Timeframe: timeframe,
            OutputFilePath: $"data/generated/dukascopy_{symbol}_{timeframe}_20230101_20240101.csv",
            OutputFileId: $"df-{Guid.NewGuid():N}",
            BarCount: bars,
            FirstBar: firstBar,
            LastBar: lastBar,
            GeneratedAt: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(record, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<GeneratedTimeframeRecord>(json, JsonOpts);

        if (deserialized is null) return false;

        return deserialized.RecordId == record.RecordId
            && deserialized.TickImportId == record.TickImportId
            && deserialized.Timeframe == record.Timeframe
            && deserialized.OutputFilePath == record.OutputFilePath
            && deserialized.OutputFileId == record.OutputFileId
            && deserialized.BarCount == record.BarCount
            && deserialized.FirstBar == record.FirstBar
            && deserialized.LastBar == record.LastBar
            && deserialized.GeneratedAt == record.GeneratedAt;
    }
}
