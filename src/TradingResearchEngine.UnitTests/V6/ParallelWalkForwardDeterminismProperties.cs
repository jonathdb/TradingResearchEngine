using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 12: ParallelWalkForwardDeterminism
/// Same seed + config → identical window results sorted by window index.
/// **Validates: Requirements 8.4, 8.6**
/// </summary>
public class ParallelWalkForwardDeterminismProperties
{
    [Property(MaxTest = 100)]
    public bool PrecomputeWindows_SameInputs_IdenticalOutput(
        PositiveInt isLengthDays, PositiveInt oosLengthDays, PositiveInt stepDays, PositiveInt totalDays)
    {
        // Constrain to reasonable values
        int isLen = (isLengthDays.Get % 60) + 5;   // 5-64 days
        int oosLen = (oosLengthDays.Get % 30) + 2;  // 2-31 days
        int step = (stepDays.Get % 20) + 1;          // 1-20 days
        int total = isLen + oosLen + (totalDays.Get % 200); // enough for at least 1 window

        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(isLen),
            OutOfSampleLength = TimeSpan.FromDays(oosLen),
            StepSize = TimeSpan.FromDays(step),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(total);

        // Run twice with identical inputs
        var specs1 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);
        var specs2 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        if (specs1.Count != specs2.Count) return false;

        for (int i = 0; i < specs1.Count; i++)
        {
            if (specs1[i].WindowIndex != specs2[i].WindowIndex) return false;
            if (specs1[i].IsStart != specs2[i].IsStart) return false;
            if (specs1[i].IsEnd != specs2[i].IsEnd) return false;
            if (specs1[i].OosStart != specs2[i].OosStart) return false;
            if (specs1[i].OosEnd != specs2[i].OosEnd) return false;
        }

        return true;
    }

    [Property(MaxTest = 100)]
    public bool PrecomputeWindows_ResultsSortedByWindowIndex(
        PositiveInt isLengthDays, PositiveInt oosLengthDays, PositiveInt stepDays, PositiveInt totalDays)
    {
        int isLen = (isLengthDays.Get % 60) + 5;
        int oosLen = (oosLengthDays.Get % 30) + 2;
        int step = (stepDays.Get % 20) + 1;
        int total = isLen + oosLen + (totalDays.Get % 200);

        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(isLen),
            OutOfSampleLength = TimeSpan.FromDays(oosLen),
            StepSize = TimeSpan.FromDays(step),
            Mode = WalkForwardMode.Rolling
        };

        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(total);

        var specs = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        // Verify sorted by window index
        for (int i = 0; i < specs.Count; i++)
        {
            if (specs[i].WindowIndex != i) return false;
        }

        return true;
    }

    [Property(MaxTest = 100)]
    public bool PrecomputeWindows_Anchored_SameInputs_IdenticalOutput(
        PositiveInt isLengthDays, PositiveInt oosLengthDays, PositiveInt stepDays, PositiveInt totalDays)
    {
        int isLen = (isLengthDays.Get % 60) + 5;
        int oosLen = (oosLengthDays.Get % 30) + 2;
        int step = (stepDays.Get % 20) + 1;
        int total = isLen + oosLen + (totalDays.Get % 200);

        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(isLen),
            OutOfSampleLength = TimeSpan.FromDays(oosLen),
            StepSize = TimeSpan.FromDays(step),
            Mode = WalkForwardMode.Anchored
        };

        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from + TimeSpan.FromDays(total);

        var specs1 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);
        var specs2 = WalkForwardWorkflow.PrecomputeWindows(options, from, to);

        if (specs1.Count != specs2.Count) return false;

        for (int i = 0; i < specs1.Count; i++)
        {
            if (specs1[i] != specs2[i]) return false;
        }

        return true;
    }
}
