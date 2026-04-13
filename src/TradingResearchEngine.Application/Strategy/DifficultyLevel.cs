namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Difficulty classification for strategy templates, surfaced in the builder UX
/// as a badge on family cards.
/// </summary>
public enum DifficultyLevel
{
    /// <summary>Suitable for researchers new to the strategy family.</summary>
    Beginner,
    /// <summary>Requires some familiarity with the underlying concepts.</summary>
    Intermediate,
    /// <summary>Requires deep understanding of the strategy mechanics and risks.</summary>
    Advanced
}
