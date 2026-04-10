using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.UnitTests.V4;

public class V4SealedTestSetTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // --- SealedTestSetGuard ---

    [Fact]
    public void SealedTestSetGuard_OverlappingRange_Throws()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var version = MakeVersion(sealed_);

        var ex = Assert.Throws<SealedTestSetViolationException>(() =>
            SealedTestSetGuard.Validate(version,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Contains("sealed test set", ex.Message);
    }

    [Fact]
    public void SealedTestSetGuard_NonOverlappingRange_DoesNotThrow()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var version = MakeVersion(sealed_);

        // Study ends exactly at sealed start — no overlap (half-open)
        SealedTestSetGuard.Validate(version,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        // No exception = pass
    }

    [Fact]
    public void SealedTestSetGuard_NoSealedSet_DoesNotThrow()
    {
        var version = MakeVersion(sealedTestSet: null);

        SealedTestSetGuard.Validate(version,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero));
        // No exception = pass
    }

    [Fact]
    public void SealedTestSetGuard_ValidateConfig_ExtractsDateRange()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var version = MakeVersion(sealed_);
        var config = MakeConfig(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<SealedTestSetViolationException>(() =>
            SealedTestSetGuard.ValidateConfig(version, config));
    }

    // --- StudyRecord partial extensions ---

    [Fact]
    public void StudyRecord_AsCancelled_SetsPartialFields()
    {
        var study = new StudyRecord("s1", "v1", StudyType.MonteCarlo, StudyStatus.Running, T0);
        var cancelled = study.AsCancelled(347, 1000);

        Assert.Equal(StudyStatus.Cancelled, cancelled.Status);
        Assert.True(cancelled.IsPartial);
        Assert.Equal(347, cancelled.CompletedCount);
        Assert.Equal(1000, cancelled.TotalCount);
    }

    [Fact]
    public void StudyRecord_AsCompleted_ClearsPartial()
    {
        var study = new StudyRecord("s1", "v1", StudyType.WalkForward, StudyStatus.Running, T0);
        var completed = study.AsCompleted(8);

        Assert.Equal(StudyStatus.Completed, completed.Status);
        Assert.False(completed.IsPartial);
        Assert.Equal(8, completed.CompletedCount);
        Assert.Equal(8, completed.TotalCount);
    }

    [Fact]
    public void StudyRecord_HasEnoughForVerdict_MonteCarlo_Requires200()
    {
        var study = new StudyRecord("s1", "v1", StudyType.MonteCarlo, StudyStatus.Cancelled, T0,
            IsPartial: true, CompletedCount: 150, TotalCount: 1000);
        Assert.False(study.HasEnoughForVerdict());

        var enough = study with { CompletedCount = 200 };
        Assert.True(enough.HasEnoughForVerdict());
    }

    [Fact]
    public void StudyRecord_HasEnoughForVerdict_WalkForward_Requires1()
    {
        var study = new StudyRecord("s1", "v1", StudyType.WalkForward, StudyStatus.Cancelled, T0,
            IsPartial: true, CompletedCount: 0, TotalCount: 8);
        Assert.False(study.HasEnoughForVerdict());

        var enough = study with { CompletedCount = 1 };
        Assert.True(enough.HasEnoughForVerdict());
    }

    // --- Helpers ---

    private static StrategyVersion MakeVersion(DateRangeConstraint? sealedTestSet) =>
        new("v1", "s1", 1, new Dictionary<string, object>(),
            MakeConfig(), T0, SealedTestSet: sealedTestSet);

    private static ScenarioConfig MakeConfig(
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var opts = new Dictionary<string, object>();
        if (from.HasValue) opts["From"] = from.Value;
        if (to.HasValue) opts["To"] = to.Value;

        return new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
            opts, "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);
    }
}
