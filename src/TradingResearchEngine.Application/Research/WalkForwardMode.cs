namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Controls how the training window moves in a walk-forward study.
/// </summary>
public enum WalkForwardMode
{
    /// <summary>Training window slides forward: both start and end advance each step.</summary>
    Rolling,

    /// <summary>Training start stays fixed; training end advances each step, expanding the window.</summary>
    Anchored
}
