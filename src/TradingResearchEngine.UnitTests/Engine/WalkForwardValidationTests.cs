using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Engine;

/// <summary>
/// Tests for <see cref="PreflightValidator.ValidateWalkForward"/> walk-forward pre-run validation.
/// </summary>
public sealed class WalkForwardValidationTests
{
    private static readonly DateTimeOffset DataFrom = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidateWalkForward_SufficientDataForMultipleWindows_ReturnsOk()
    {
        // 365 days of data, 90-day IS + 30-day OOS + 30-day step → multiple windows
        var dataTo = DataFrom.AddDays(365);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(30)
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.True(result.IsValid);
        Assert.NotNull(result.WindowCount);
        Assert.True(result.WindowCount >= 2);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void ValidateWalkForward_InsufficientData_ReturnsFail()
    {
        // Only 50 days of data, but need 90 + 30 = 120 days minimum
        var dataTo = DataFrom.AddDays(50);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(30)
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.False(result.IsValid);
        Assert.Null(result.WindowCount);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("insufficient", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InSampleLength", result.ErrorMessage);
        Assert.Contains("OutOfSampleLength", result.ErrorMessage);
    }

    [Fact]
    public void ValidateWalkForward_ExactlyOneWindow_ReturnsWarn()
    {
        // Exactly 120 days = 90 IS + 30 OOS → 1 window, step doesn't allow a second
        var dataTo = DataFrom.AddDays(120);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(30)
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.WindowCount);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("fewer than 2 windows", result.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateWalkForward_ExactlyTwoWindows_ReturnsOkNoWarning()
    {
        // 150 days = first window at 0 (IS 0-90, OOS 90-120), second at step 30 (IS 30-120, OOS 120-150)
        var dataTo = DataFrom.AddDays(150);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(30)
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.WindowCount);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void ValidateWalkForward_AnchoredMode_ComputesCorrectWindowCount()
    {
        // Anchored mode: IS always starts at dataFrom, grows with each step
        var dataTo = DataFrom.AddDays(365);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(60),
            Mode = WalkForwardMode.Anchored
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.True(result.IsValid);
        Assert.NotNull(result.WindowCount);
        Assert.True(result.WindowCount >= 2);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void ValidateWalkForward_ErrorMessage_StatesMinimumRequiredLength()
    {
        var dataTo = DataFrom.AddDays(10);
        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(252),
            OutOfSampleLength = TimeSpan.FromDays(63),
            StepSize = TimeSpan.FromDays(63)
        };

        var result = PreflightValidator.ValidateWalkForward(options, DataFrom, dataTo);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        // Should mention the minimum required (252 + 63 = 315 days)
        Assert.Contains("InSampleLength", result.ErrorMessage);
        Assert.Contains("OutOfSampleLength", result.ErrorMessage);
        Assert.Contains("insufficient", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WalkForwardValidation_Fail_SetsPropertiesCorrectly()
    {
        var result = WalkForwardValidation.Fail("Test error");

        Assert.False(result.IsValid);
        Assert.Null(result.WindowCount);
        Assert.Equal("Test error", result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void WalkForwardValidation_Warn_SetsPropertiesCorrectly()
    {
        var result = WalkForwardValidation.Warn(1, "Test warning");

        Assert.True(result.IsValid);
        Assert.Equal(1, result.WindowCount);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("Test warning", result.WarningMessage);
    }

    [Fact]
    public void WalkForwardValidation_Ok_SetsPropertiesCorrectly()
    {
        var result = WalkForwardValidation.Ok(5);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.WindowCount);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }
}
