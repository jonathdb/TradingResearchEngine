// Feature: tick-data-first-import, Property 8: TickImportRecord JSON Round-Trip
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 8: For any valid TickImportRecord, serialize to JSON then deserialize produces equivalent record.
/// **Validates: Requirements 9.4**
/// </summary>
public class TickImportRecordProperties
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] Symbols = { "EURUSD", "GBPUSD", "USDJPY", "XAUUSD", "AUDUSD" };
    private static readonly TickImportStatus[] Statuses = Enum.GetValues<TickImportStatus>();

    [Property(MaxTest = 100)]
    public bool JsonRoundTrip_ProducesEquivalentRecord(
        PositiveInt symbolIdx, PositiveInt statusIdx, PositiveInt dayOffset,
        PositiveInt rangeDays, PositiveInt tickCount, bool hasError, bool hasCompleted)
    {
        var symbol = Symbols[symbolIdx.Get % Symbols.Length];
        var status = Statuses[statusIdx.Get % Statuses.Length];
        var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset.Get % 1500);
        var end = start.AddDays((rangeDays.Get % 365) + 1);
        var importId = $"tick-{Guid.NewGuid():N}";
        var createdAt = DateTimeOffset.UtcNow;

        var record = new TickImportRecord(
            ImportId: importId,
            Source: "Dukascopy",
            Symbol: symbol,
            RequestedStart: start,
            RequestedEnd: end,
            Status: status,
            TotalTickCount: status == TickImportStatus.Completed ? (long)(tickCount.Get % 1000000) : null,
            ErrorDetail: hasError && status == TickImportStatus.Failed ? "Network timeout" : null,
            CreatedAt: createdAt,
            CompletedAt: hasCompleted ? createdAt.AddMinutes(5) : null);

        var json = JsonSerializer.Serialize(record, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<TickImportRecord>(json, JsonOpts);

        if (deserialized is null) return false;

        return deserialized.ImportId == record.ImportId
            && deserialized.Source == record.Source
            && deserialized.Symbol == record.Symbol
            && deserialized.RequestedStart == record.RequestedStart
            && deserialized.RequestedEnd == record.RequestedEnd
            && deserialized.Status == record.Status
            && deserialized.TotalTickCount == record.TotalTickCount
            && deserialized.ErrorDetail == record.ErrorDetail
            && deserialized.CreatedAt == record.CreatedAt
            && deserialized.CompletedAt == record.CompletedAt;
    }
}
