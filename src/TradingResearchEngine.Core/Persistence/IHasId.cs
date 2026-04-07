namespace TradingResearchEngine.Core.Persistence;

/// <summary>Marks a type as having a stable string identifier for persistence.</summary>
public interface IHasId
{
    /// <summary>The unique identifier for this entity.</summary>
    string Id { get; }
}
