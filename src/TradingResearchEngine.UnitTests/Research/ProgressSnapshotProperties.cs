using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

// Feature: research-platform-v9, Property 4: ProgressSnapshot ETA Formula

/// <summary>
/// Property-based tests verifying that <see cref="ProgressSnapshot.EstimatedTimeRemaining"/>
/// correctly computes the linear extrapolation ETA.
/// </summary>
public sealed class ProgressSnapshotProperties
{
    /// <summary>
    /// For any snapshot where Current > 0 and Total > 0, EstimatedTimeRemaining equals
    /// (ElapsedTime / Current) * (Total - Current).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ETA_WhenCurrentAndTotalPositive_EqualsLinearExtrapolation(
        PositiveInt currentWrap, PositiveInt totalWrap, PositiveInt elapsedSecondsWrap)
    {
        var current = (currentWrap.Get % 1000) + 1; // 1–1000
        var total = current + (totalWrap.Get % 5000); // total >= current
        var elapsedSeconds = (elapsedSecondsWrap.Get % 3600) + 1; // 1–3600 seconds
        var elapsed = TimeSpan.FromSeconds(elapsedSeconds);

        var snapshot = new ProgressSnapshot(
            Current: current,
            Total: total,
            Percentage: (decimal)current / total * 100m,
            Stage: "Testing",
            CurrentItemLabel: null,
            ElapsedTime: elapsed,
            Warnings: Array.Empty<string>());

        var eta = snapshot.EstimatedTimeRemaining;

        if (eta is null) return false;

        // Expected: (elapsed / current) * (total - current)
        var expectedTicks = (long)(elapsed.Ticks / (double)current * (total - current));
        var expected = TimeSpan.FromTicks(expectedTicks);

        // Allow 1 tick tolerance for floating-point rounding
        return Math.Abs(eta.Value.Ticks - expected.Ticks) <= 1;
    }

    /// <summary>
    /// When Current is 0, EstimatedTimeRemaining is null (indeterminate).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ETA_WhenCurrentIsZero_ReturnsNull(PositiveInt totalWrap, PositiveInt elapsedWrap)
    {
        var total = (totalWrap.Get % 1000) + 1;
        var elapsed = TimeSpan.FromSeconds((elapsedWrap.Get % 3600) + 1);

        var snapshot = new ProgressSnapshot(
            Current: 0,
            Total: total,
            Percentage: 0m,
            Stage: "Starting",
            CurrentItemLabel: null,
            ElapsedTime: elapsed,
            Warnings: Array.Empty<string>());

        return snapshot.EstimatedTimeRemaining is null;
    }

    /// <summary>
    /// When Total is 0 (indeterminate), EstimatedTimeRemaining is null.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ETA_WhenTotalIsZero_ReturnsNull(PositiveInt currentWrap, PositiveInt elapsedWrap)
    {
        var current = (currentWrap.Get % 1000) + 1;
        var elapsed = TimeSpan.FromSeconds((elapsedWrap.Get % 3600) + 1);

        var snapshot = new ProgressSnapshot(
            Current: current,
            Total: 0,
            Percentage: 0m,
            Stage: "Indeterminate",
            CurrentItemLabel: null,
            ElapsedTime: elapsed,
            Warnings: Array.Empty<string>());

        return snapshot.EstimatedTimeRemaining is null;
    }

    /// <summary>
    /// WarningCount equals the number of items in the Warnings list.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WarningCount_EqualsListCount(NonNegativeInt countWrap)
    {
        var count = countWrap.Get % 20; // 0–19 warnings
        var warnings = Enumerable.Range(0, count).Select(i => $"Warning {i}").ToList();

        var snapshot = new ProgressSnapshot(
            Current: 50,
            Total: 100,
            Percentage: 50m,
            Stage: "Running",
            CurrentItemLabel: null,
            ElapsedTime: TimeSpan.FromMinutes(5),
            Warnings: warnings);

        return snapshot.WarningCount == count;
    }
}
