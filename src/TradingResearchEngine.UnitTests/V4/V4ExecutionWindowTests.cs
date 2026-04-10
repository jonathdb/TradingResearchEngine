using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.UnitTests.V4;

public class V4ExecutionWindowTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_ValidRange_Succeeds()
    {
        var version = MakeVersion();
        var result = ExecutionWindowEditor.Validate(version, "Daily",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.Success);
        Assert.NotNull(result.UpdatedVersion);
        Assert.Equal("Daily", result.UpdatedVersion.BaseScenarioConfig.Timeframe);
        Assert.Equal(252, result.UpdatedVersion.BaseScenarioConfig.BarsPerYear);
    }

    [Fact]
    public void Validate_StartAfterEnd_Fails()
    {
        var version = MakeVersion();
        var result = ExecutionWindowEditor.Validate(version, "Daily",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("before end date"));
    }

    [Fact]
    public void Validate_OutOfDataFileRange_Fails()
    {
        var version = MakeVersion();
        var dataFile = new DataFileRecord("f1", "test.csv", "/test.csv", "EURUSD", "Daily",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            2515, ValidationStatus.Valid, null, T0);

        var result = ExecutionWindowEditor.Validate(version, "Daily",
            new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            dataFile);

        Assert.False(result.Success);
        Assert.True(result.Errors.Count >= 2); // both start and end out of range
    }

    [Fact]
    public void Validate_SealedSetExcluded_Fails()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);
        var version = MakeVersion(sealed_);

        // End date before sealed set start = would exclude sealed set
        var result = ExecutionWindowEditor.Validate(version, "Daily",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("sealed test set"));
    }

    [Fact]
    public void Validate_H4Timeframe_SetsBarsPerYear()
    {
        var version = MakeVersion();
        var result = ExecutionWindowEditor.Validate(version, "H4",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.Success);
        Assert.Equal(1512, result.UpdatedVersion!.BaseScenarioConfig.BarsPerYear);
    }

    [Fact]
    public void GetCurrentWindow_LegacyConfig_InfersTimeframe()
    {
        var version = MakeVersion(); // BarsPerYear=252, no Timeframe field
        var (tf, start, end) = ExecutionWindowEditor.GetCurrentWindow(version);

        Assert.Equal("Daily", tf); // inferred from BarsPerYear=252
    }

    [Fact]
    public void GetCurrentWindow_ExplicitTimeframe_UsesIt()
    {
        var config = MakeConfig() with { Timeframe = "H4", BarsPerYear = 1512 };
        var version = new StrategyVersion("v1", "s1", 1, new(), config, T0);
        var (tf, _, _) = ExecutionWindowEditor.GetCurrentWindow(version);

        Assert.Equal("H4", tf);
    }

    [Fact]
    public void EstimateBarCount_Daily_ReasonableEstimate()
    {
        var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var bars = ExecutionWindowEditor.EstimateBarCount("Daily", start, end);

        Assert.NotNull(bars);
        Assert.True(bars > 1000 && bars < 1500, $"Expected ~1260 daily bars, got {bars}");
    }

    [Fact]
    public void Validate_SaveUpdatesVersionConfigOnly()
    {
        var version = MakeVersion();
        var originalParams = version.Parameters;

        var result = ExecutionWindowEditor.Validate(version, "H1",
            new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.Success);
        // Parameters unchanged
        Assert.Same(originalParams, result.UpdatedVersion!.Parameters);
        // VersionId unchanged
        Assert.Equal(version.StrategyVersionId, result.UpdatedVersion.StrategyVersionId);
        // Config updated
        Assert.Equal("H1", result.UpdatedVersion.BaseScenarioConfig.Timeframe);
        Assert.Equal(6048, result.UpdatedVersion.BaseScenarioConfig.BarsPerYear);
    }

    [Fact]
    public void ScenarioConfig_Timeframe_NullByDefault_LegacyCompat()
    {
        var config = MakeConfig();
        Assert.Null(config.Timeframe); // backwards compatible
    }

    // --- Helpers ---

    private static StrategyVersion MakeVersion(DateRangeConstraint? sealedTestSet = null) =>
        new("v1", "s1", 1, new Dictionary<string, object> { ["fast"] = 10 },
            MakeConfig(), T0, SealedTestSet: sealedTestSet);

    private static ScenarioConfig MakeConfig() =>
        new("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);
}
