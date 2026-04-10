using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// A user-owned, named research concept. Represents "my EURUSD mean reversion idea"
/// as a persistent entity that spans multiple versions, runs, and studies.
/// </summary>
public sealed record StrategyIdentity(
    string StrategyId,
    string StrategyName,
    string StrategyType,
    DateTimeOffset CreatedAt,
    string? Description = null,
    /// <summary>V4: Research lifecycle stage. Defaults to <see cref="DevelopmentStage.Exploring"/> for backwards-compatible JSON deserialization.</summary>
    DevelopmentStage Stage = DevelopmentStage.Exploring,
    /// <summary>V4: User's hypothesis for why this strategy should work. Prompted at creation.</summary>
    string? Hypothesis = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StrategyId;
}
