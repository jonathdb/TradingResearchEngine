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
    string? Description = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StrategyId;
}
