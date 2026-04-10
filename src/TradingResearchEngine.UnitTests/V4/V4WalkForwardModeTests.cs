using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V4;

public class V4WalkForwardModeTests
{
    [Fact]
    public void WalkForwardOptions_EffectiveMode_DefaultsToRolling()
    {
        var opts = new WalkForwardOptions();
        Assert.Equal(WalkForwardMode.Rolling, opts.EffectiveMode);
    }

    [Fact]
    public void WalkForwardOptions_EffectiveMode_AnchoredBooleanFallback()
    {
        var opts = new WalkForwardOptions { AnchoredWindow = true };
        Assert.Equal(WalkForwardMode.Anchored, opts.EffectiveMode);
    }

    [Fact]
    public void WalkForwardOptions_EffectiveMode_EnumOverridesTakePrecedence()
    {
        var opts = new WalkForwardOptions
        {
            AnchoredWindow = false,
            Mode = WalkForwardMode.Anchored
        };
        Assert.Equal(WalkForwardMode.Anchored, opts.EffectiveMode);
    }

    [Fact]
    public void WalkForwardOptions_EffectiveMode_RollingEnumOverridesAnchoredBool()
    {
        var opts = new WalkForwardOptions
        {
            AnchoredWindow = true,
            Mode = WalkForwardMode.Rolling
        };
        Assert.Equal(WalkForwardMode.Rolling, opts.EffectiveMode);
    }

    [Fact]
    public void AnchoredMode_IsStartAlwaysDataFrom()
    {
        // Simulate the window calculation logic from WalkForwardWorkflow
        var dataFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var isLength = TimeSpan.FromDays(365);
        var stepSize = TimeSpan.FromDays(90);

        var starts = new List<DateTimeOffset>();
        var ends = new List<DateTimeOffset>();

        for (int i = 0; i < 4; i++)
        {
            var currentOffset = TimeSpan.FromTicks(stepSize.Ticks * i);
            var isStart = dataFrom; // Anchored: always dataFrom
            var isEnd = dataFrom + isLength + currentOffset; // Expanding window
            starts.Add(isStart);
            ends.Add(isEnd);
        }

        // All starts should be the same (anchored)
        Assert.All(starts, s => Assert.Equal(dataFrom, s));

        // Ends should be increasing (expanding window)
        for (int i = 1; i < ends.Count; i++)
            Assert.True(ends[i] > ends[i - 1], "IS end should advance in anchored mode");
    }

    [Fact]
    public void RollingMode_IsStartAdvances()
    {
        var dataFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var isLength = TimeSpan.FromDays(365);
        var stepSize = TimeSpan.FromDays(90);

        var starts = new List<DateTimeOffset>();
        var ends = new List<DateTimeOffset>();

        for (int i = 0; i < 4; i++)
        {
            var currentOffset = TimeSpan.FromTicks(stepSize.Ticks * i);
            var isStart = dataFrom + currentOffset; // Rolling: start advances
            var isEnd = isStart + isLength; // Fixed-length window
            starts.Add(isStart);
            ends.Add(isEnd);
        }

        // Starts should be increasing
        for (int i = 1; i < starts.Count; i++)
            Assert.True(starts[i] > starts[i - 1], "IS start should advance in rolling mode");

        // Window length should be constant
        for (int i = 0; i < starts.Count; i++)
            Assert.Equal(isLength, ends[i] - starts[i]);
    }
}
