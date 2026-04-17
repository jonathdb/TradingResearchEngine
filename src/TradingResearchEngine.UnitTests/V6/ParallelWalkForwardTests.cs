using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Tests for V6 parallel walk-forward execution.
/// Validates window pre-computation, determinism, and cancellation.
/// </summary>
public class ParallelWalkForwardTests
{
    [Fact]
    public void PrecomputeWindows_Rolling_CorrectWindowCount()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(100);

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        // Window 0: IS [0,30), OOS [30,40) — fits
        // Window 1: IS [10,40), OOS [40,50) — fits
        // ...
        // Window 6: IS [60,90), OOS [90,100) — fits
        // Window 7: IS [70,100), OOS [100,110) — exceeds
        Assert.Equal(7, specs.Count);
    }

    [Fact]
    public void PrecomputeWindows_Anchored_CorrectWindowCount()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Anchored
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(100);

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        // Anchored: IS always starts at from
        // Window 0: IS [0,30), OOS [30,40) — fits
        // Window 1: IS [0,40), OOS [40,50) — fits
        // ...
        // Window 6: IS [0,90), OOS [90,100) — fits
        // Window 7: IS [0,100), OOS [100,110) — exceeds
        Assert.Equal(7, specs.Count);
    }

    [Fact]
    public void PrecomputeWindows_WindowsSortedByIndex()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(80);

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        for (int i = 0; i < specs.Count; i++)
        {
            Assert.Equal(i, specs[i].WindowIndex);
        }
    }

    [Fact]
    public void PrecomputeWindows_InsufficientData_ReturnsEmpty()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(35); // Not enough for IS + OOS

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        Assert.Empty(specs);
    }

    [Fact]
    public void PrecomputeWindows_ExactFit_SingleWindow()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(40); // Exactly IS + OOS

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        Assert.Single(specs);
        Assert.Equal(0, specs[0].WindowIndex);
    }

    [Fact]
    public void PrecomputeWindows_SameConfig_ProducesSameWindows()
    {
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(30),
            OutOfSampleLength = TimeSpan.FromDays(10),
            StepSize = TimeSpan.FromDays(10),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(100);

        var specs1 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);
        var specs2 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        Assert.Equal(specs1.Count, specs2.Count);
        for (int i = 0; i < specs1.Count; i++)
        {
            Assert.Equal(specs1[i].WindowIndex, specs2[i].WindowIndex);
            Assert.Equal(specs1[i].IsStart, specs2[i].IsStart);
            Assert.Equal(specs1[i].IsEnd, specs2[i].IsEnd);
            Assert.Equal(specs1[i].OosStart, specs2[i].OosStart);
            Assert.Equal(specs1[i].OosEnd, specs2[i].OosEnd);
        }
    }
}
