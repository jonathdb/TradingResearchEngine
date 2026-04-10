using System.Text.Json;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.UnitTests.V4;

public class V4DomainModelTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // --- StrategyIdentity ---

    [Fact]
    public void StrategyIdentity_MissingStage_DefaultsToExploring()
    {
        // Simulate V3 JSON without Stage or Hypothesis fields
        var v3Json = """
        {
            "StrategyId": "test-id",
            "StrategyName": "Test",
            "StrategyType": "sma-crossover",
            "CreatedAt": "2024-01-01T00:00:00+00:00",
            "Description": null
        }
        """;

        var deserialized = JsonSerializer.Deserialize<StrategyIdentity>(v3Json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Equal(DevelopmentStage.Exploring, deserialized.Stage);
        Assert.Null(deserialized.Hypothesis);
    }

    [Fact]
    public void StrategyIdentity_WithStageAndHypothesis_RoundTrips()
    {
        var identity = new StrategyIdentity("id", "Name", "sma-crossover", T0,
            "Desc", DevelopmentStage.Validating, "Mean reversion works in range-bound markets");

        var json = JsonSerializer.Serialize(identity, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<StrategyIdentity>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(DevelopmentStage.Validating, deserialized.Stage);
        Assert.Equal("Mean reversion works in range-bound markets", deserialized.Hypothesis);
    }

    // --- StrategyVersion ---

    [Fact]
    public void StrategyVersion_SealedTestSet_Serializes()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var config = MakeConfig();
        var version = new StrategyVersion("v1", "s1", 1,
            new Dictionary<string, object> { ["fast"] = 10 }, config, T0,
            "Initial", TotalTrialsRun: 5, SealedTestSet: sealed_);

        var json = JsonSerializer.Serialize(version, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(5, deserialized.TotalTrialsRun);
        Assert.NotNull(deserialized.SealedTestSet);
        Assert.True(deserialized.SealedTestSet.Value.IsSealed);
        Assert.Equal(sealed_.Start, deserialized.SealedTestSet.Value.Start);
    }

    [Fact]
    public void StrategyVersion_MissingNewFields_DefaultsCorrectly()
    {
        // Simulate V3 JSON without TotalTrialsRun or SealedTestSet
        var v3Json = """
        {
            "StrategyVersionId": "v1",
            "StrategyId": "s1",
            "VersionNumber": 1,
            "Parameters": { "fast": 10 },
            "BaseScenarioConfig": {
                "ScenarioId": "test",
                "Description": "Test",
                "ReplayMode": 0,
                "DataProviderType": "csv",
                "DataProviderOptions": {},
                "StrategyType": "test",
                "StrategyParameters": {},
                "RiskParameters": {},
                "SlippageModelType": "Zero",
                "CommissionModelType": "Zero",
                "InitialCash": 100000,
                "AnnualRiskFreeRate": 0.02,
                "RandomSeed": null,
                "ResearchWorkflowType": null,
                "ResearchWorkflowOptions": null,
                "PropFirmOptions": null
            },
            "CreatedAt": "2024-01-01T00:00:00+00:00",
            "ChangeNote": null
        }
        """;

        var deserialized = JsonSerializer.Deserialize<StrategyVersion>(v3Json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.TotalTrialsRun);
        Assert.Null(deserialized.SealedTestSet);
    }

    // --- StudyRecord ---

    [Fact]
    public void StudyRecord_PartialFields_Serialize()
    {
        var study = new StudyRecord("study-1", "v1", StudyType.MonteCarlo,
            StudyStatus.Cancelled, T0, IsPartial: true, CompletedCount: 347, TotalCount: 1000);

        var json = JsonSerializer.Serialize(study, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<StudyRecord>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsPartial);
        Assert.Equal(347, deserialized.CompletedCount);
        Assert.Equal(1000, deserialized.TotalCount);
    }

    [Fact]
    public void StudyRecord_MissingPartialFields_DefaultsToFalse()
    {
        var v3Json = """
        {
            "StudyId": "study-1",
            "StrategyVersionId": "v1",
            "Type": 0,
            "Status": 1,
            "CreatedAt": "2024-01-01T00:00:00+00:00"
        }
        """;

        var deserialized = JsonSerializer.Deserialize<StudyRecord>(v3Json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsPartial);
        Assert.Equal(0, deserialized.CompletedCount);
        Assert.Equal(0, deserialized.TotalCount);
    }

    [Fact]
    public void StudyType_NewEntries_Exist()
    {
        Assert.True(Enum.IsDefined(typeof(StudyType), StudyType.AnchoredWalkForward));
        Assert.True(Enum.IsDefined(typeof(StudyType), StudyType.CombinatorialPurgedCV));
        Assert.True(Enum.IsDefined(typeof(StudyType), StudyType.RegimeSegmentation));
    }

    // --- DataFileRecord ---

    [Fact]
    public void DataFileRecord_RoundTrip_Json()
    {
        var record = new DataFileRecord(
            "file-1", "EURUSD_Daily.csv", "/data/EURUSD_Daily.csv",
            "EURUSD", "Daily",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            2515, ValidationStatus.Valid, null, T0);

        var json = JsonSerializer.Serialize(record, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<DataFileRecord>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal("file-1", deserialized.FileId);
        Assert.Equal("EURUSD", deserialized.DetectedSymbol);
        Assert.Equal(2515, deserialized.BarCount);
        Assert.Equal(ValidationStatus.Valid, deserialized.ValidationStatus);
        Assert.Equal("file-1", deserialized.Id);
    }

    [Fact]
    public void DataFileRecord_InvalidStatus_RoundTrips()
    {
        var record = new DataFileRecord(
            "file-2", "bad.csv", "/data/bad.csv",
            null, null, null, null, 0,
            ValidationStatus.Invalid,
            "Missing required columns: Open, High",
            T0);

        var json = JsonSerializer.Serialize(record, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<DataFileRecord>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(ValidationStatus.Invalid, deserialized.ValidationStatus);
        Assert.Equal("Missing required columns: Open, High", deserialized.ValidationError);
    }

    // --- WalkForwardMode ---

    [Fact]
    public void WalkForwardMode_Values_Exist()
    {
        Assert.True(Enum.IsDefined(typeof(WalkForwardMode), WalkForwardMode.Rolling));
        Assert.True(Enum.IsDefined(typeof(WalkForwardMode), WalkForwardMode.Anchored));
    }

    // --- Helper ---

    private static ScenarioConfig MakeConfig() =>
        new("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);
}
