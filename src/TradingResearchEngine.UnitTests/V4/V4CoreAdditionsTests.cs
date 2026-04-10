using System.Text.Json;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V4;

public class V4CoreAdditionsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // --- BacktestResult new fields ---

    [Fact]
    public void BacktestResult_FailureDetail_NullByDefault()
    {
        var result = MakeResult();
        Assert.Null(result.FailureDetail);
        Assert.Null(result.DeflatedSharpeRatio);
        Assert.Null(result.TrialCount);
    }

    [Fact]
    public void BacktestResult_WithFailureDetail_RoundTrips()
    {
        var result = MakeResult() with
        {
            Status = BacktestStatus.Failed,
            FailureDetail = "NullReferenceException in OnBar at bar 142"
        };

        var json = JsonSerializer.Serialize(result, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<BacktestResult>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(BacktestStatus.Failed, deserialized.Status);
        Assert.Equal("NullReferenceException in OnBar at bar 142", deserialized.FailureDetail);
    }

    [Fact]
    public void BacktestResult_WithDsrAndTrialCount_RoundTrips()
    {
        var result = MakeResult() with
        {
            DeflatedSharpeRatio = 1.12m,
            TrialCount = 5
        };

        var json = JsonSerializer.Serialize(result, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<BacktestResult>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(1.12m, deserialized.DeflatedSharpeRatio);
        Assert.Equal(5, deserialized.TrialCount);
    }

    [Fact]
    public void BacktestResult_LegacyJson_MissingNewFields_DeserializesToNull()
    {
        // Simulate a V3 JSON that has no FailureDetail/DSR/TrialCount fields
        var result = MakeResult();
        var json = JsonSerializer.Serialize(result, JsonOpts);

        // Verify the new fields are null when not present
        var deserialized = JsonSerializer.Deserialize<BacktestResult>(json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.FailureDetail);
        Assert.Null(deserialized.DeflatedSharpeRatio);
        Assert.Null(deserialized.TrialCount);
    }

    // --- DateRangeConstraint ---

    [Fact]
    public void DateRangeConstraint_Contains_StartInclusive()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var constraint = new DateRangeConstraint(start, end, IsSealed: true);

        Assert.True(constraint.Contains(start)); // Start is inclusive
    }

    [Fact]
    public void DateRangeConstraint_Contains_EndExclusive()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var constraint = new DateRangeConstraint(start, end, IsSealed: true);

        Assert.False(constraint.Contains(end)); // End is exclusive
    }

    [Fact]
    public void DateRangeConstraint_Contains_MidpointTrue()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var constraint = new DateRangeConstraint(start, end, IsSealed: true);

        var mid = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.True(constraint.Contains(mid));
    }

    [Fact]
    public void DateRangeConstraint_Contains_BeforeStartFalse()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var constraint = new DateRangeConstraint(start, end, IsSealed: true);

        var before = new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero);
        Assert.False(constraint.Contains(before));
    }

    [Fact]
    public void DateRangeConstraint_Overlaps_PartialOverlap_True()
    {
        var constraint = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        // Study range partially overlaps the sealed range
        var studyStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var studyEnd = new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(constraint.Overlaps(studyStart, studyEnd));
    }

    [Fact]
    public void DateRangeConstraint_Overlaps_NoOverlap_False()
    {
        var constraint = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        // Study range ends before sealed range starts
        var studyStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var studyEnd = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero); // exactly at sealed start = no overlap (half-open)

        Assert.False(constraint.Overlaps(studyStart, studyEnd));
    }

    [Fact]
    public void DateRangeConstraint_Overlaps_FullyContained_True()
    {
        var constraint = new DateRangeConstraint(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var studyStart = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var studyEnd = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(constraint.Overlaps(studyStart, studyEnd));
    }

    [Fact]
    public void DateRangeConstraint_JsonRoundTrip()
    {
        var constraint = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var json = JsonSerializer.Serialize(constraint, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<DateRangeConstraint>(json, JsonOpts);

        Assert.Equal(constraint.Start, deserialized.Start);
        Assert.Equal(constraint.End, deserialized.End);
        Assert.True(deserialized.IsSealed);
    }

    // --- Helper ---

    private static BacktestResult MakeResult()
    {
        var config = new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null);

        return new BacktestResult(
            Guid.NewGuid(), config, BacktestStatus.Completed,
            new List<EquityCurvePoint>(),
            new List<ClosedTrade>(),
            100_000m, 110_000m, 0.05m,
            1.42m, null, null, null, 23,
            0.61m, 1.87m, 500m, -300m, 142m, null, null, 3, 5, 1200);
    }
}
