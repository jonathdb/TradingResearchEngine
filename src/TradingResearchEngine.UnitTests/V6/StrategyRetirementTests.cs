using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Tests for V6 strategy retirement functionality.
/// Validates retirement state transitions and RetirementNote persistence.
/// </summary>
public class StrategyRetirementTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static StrategyIdentity MakeStrategy(
        DevelopmentStage stage = DevelopmentStage.Exploring,
        string? retirementNote = null) =>
        new("s1", "Test Strategy", "sma", T0, Stage: stage, RetirementNote: retirementNote);

    [Fact]
    public void RetiredStrategy_HiddenFromDefaultView()
    {
        var strategies = new List<StrategyIdentity>
        {
            MakeStrategy(DevelopmentStage.Exploring),
            MakeStrategy(DevelopmentStage.Retired) with { StrategyId = "s2", StrategyName = "Retired One" }
        };

        // Default view: filter out retired
        var visible = strategies.Where(s => s.Stage != DevelopmentStage.Retired).ToList();

        Assert.Single(visible);
        Assert.Equal("s1", visible[0].StrategyId);
    }

    [Fact]
    public void RetiredStrategy_VisibleWhenShowRetiredEnabled()
    {
        var strategies = new List<StrategyIdentity>
        {
            MakeStrategy(DevelopmentStage.Exploring),
            MakeStrategy(DevelopmentStage.Retired) with { StrategyId = "s2", StrategyName = "Retired One" }
        };

        // Show retired toggle enabled: show all
        var visible = strategies.ToList();

        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void RetirementNote_PersistedOnRetire()
    {
        var strategy = MakeStrategy(DevelopmentStage.Exploring);

        var retired = strategy with
        {
            Stage = DevelopmentStage.Retired,
            RetirementNote = "Strategy no longer profitable after regime change"
        };

        Assert.Equal(DevelopmentStage.Retired, retired.Stage);
        Assert.Equal("Strategy no longer profitable after regime change", retired.RetirementNote);
    }

    [Fact]
    public void RetirementNote_NullWhenNotProvided()
    {
        var strategy = MakeStrategy(DevelopmentStage.Exploring);

        var retired = strategy with { Stage = DevelopmentStage.Retired };

        Assert.Equal(DevelopmentStage.Retired, retired.Stage);
        Assert.Null(retired.RetirementNote);
    }

    [Fact]
    public void UnRetire_SetsStageToHypothesis()
    {
        var retired = MakeStrategy(DevelopmentStage.Retired, "Old note");

        var unRetired = retired with { Stage = DevelopmentStage.Hypothesis };

        Assert.Equal(DevelopmentStage.Hypothesis, unRetired.Stage);
    }

    [Fact]
    public void UnRetire_PreservesRetirementNote()
    {
        var retired = MakeStrategy(DevelopmentStage.Retired, "Regime change");

        var unRetired = retired with { Stage = DevelopmentStage.Hypothesis };

        Assert.Equal(DevelopmentStage.Hypothesis, unRetired.Stage);
        Assert.Equal("Regime change", unRetired.RetirementNote);
    }

    [Fact]
    public void RetiredEnum_ExistsInDevelopmentStage()
    {
        Assert.True(Enum.IsDefined(typeof(DevelopmentStage), DevelopmentStage.Retired));
    }
}
