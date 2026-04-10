using System.Text.Json;
using System.Text.Json.Serialization;
using TradingResearchEngine.Application.MarketData;
using Xunit;

namespace TradingResearchEngine.UnitTests.MarketData;

public class MarketDataImportRecordTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void JsonRoundTrip_CompletedRecord_PreservesAllFields()
    {
        var record = new MarketDataImportRecord(
            ImportId: "imp-001",
            Source: "Dukascopy",
            Symbol: "EURUSD",
            Timeframe: "1H",
            RequestedStart: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Status: MarketDataImportStatus.Completed,
            OutputFilePath: "datafiles/dukascopy_EURUSD_1H_20200101_20250101.csv",
            OutputFileId: "df-001",
            DownloadedChunkCount: 1300,
            TotalChunkCount: 1300,
            CandleBasis: "Bid",
            CreatedAt: new DateTimeOffset(2025, 4, 10, 12, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2025, 4, 10, 12, 5, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(record, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MarketDataImportRecord>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(record.ImportId, deserialized!.ImportId);
        Assert.Equal(record.Source, deserialized.Source);
        Assert.Equal(record.Symbol, deserialized.Symbol);
        Assert.Equal(record.Timeframe, deserialized.Timeframe);
        Assert.Equal(record.RequestedStart, deserialized.RequestedStart);
        Assert.Equal(record.RequestedEnd, deserialized.RequestedEnd);
        Assert.Equal(record.Status, deserialized.Status);
        Assert.Equal(record.OutputFilePath, deserialized.OutputFilePath);
        Assert.Equal(record.OutputFileId, deserialized.OutputFileId);
        Assert.Equal(record.DownloadedChunkCount, deserialized.DownloadedChunkCount);
        Assert.Equal(record.TotalChunkCount, deserialized.TotalChunkCount);
        Assert.Equal(record.CandleBasis, deserialized.CandleBasis);
        Assert.Equal(record.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(record.CompletedAt, deserialized.CompletedAt);
    }

    [Fact]
    public void JsonRoundTrip_FailedRecord_PreservesErrorDetail()
    {
        var record = new MarketDataImportRecord(
            ImportId: "imp-002",
            Source: "Dukascopy",
            Symbol: "GBPUSD",
            Timeframe: "Daily",
            RequestedStart: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RequestedEnd: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Status: MarketDataImportStatus.Failed,
            ErrorDetail: "Network timeout after 3 retries",
            CreatedAt: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MarketDataImportRecord>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(MarketDataImportStatus.Failed, deserialized!.Status);
        Assert.Equal("Network timeout after 3 retries", deserialized.ErrorDetail);
        Assert.Null(deserialized.OutputFilePath);
        Assert.Null(deserialized.OutputFileId);
    }

    [Fact]
    public void JsonRoundTrip_NullableFieldsDefaultCorrectly()
    {
        var record = new MarketDataImportRecord(
            ImportId: "imp-003",
            Source: "Dukascopy",
            Symbol: "USDJPY",
            Timeframe: "4H",
            RequestedStart: DateTimeOffset.UtcNow.AddYears(-1),
            RequestedEnd: DateTimeOffset.UtcNow,
            Status: MarketDataImportStatus.Running);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MarketDataImportRecord>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.OutputFilePath);
        Assert.Null(deserialized.OutputFileId);
        Assert.Null(deserialized.DownloadedChunkCount);
        Assert.Null(deserialized.TotalChunkCount);
        Assert.Null(deserialized.ErrorDetail);
        Assert.Equal("Bid", deserialized.CandleBasis);
        Assert.Null(deserialized.CompletedAt);
    }

    [Fact]
    public void Id_ReturnsImportId()
    {
        var record = new MarketDataImportRecord(
            ImportId: "imp-test",
            Source: "Dukascopy",
            Symbol: "EURUSD",
            Timeframe: "1H",
            RequestedStart: DateTimeOffset.UtcNow,
            RequestedEnd: DateTimeOffset.UtcNow,
            Status: MarketDataImportStatus.Running);

        Assert.Equal("imp-test", record.Id);
    }
}
