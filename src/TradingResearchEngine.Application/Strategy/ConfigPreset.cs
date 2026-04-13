using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>Category of a configuration preset.</summary>
public enum PresetCategory
{
    /// <summary>Zero-cost, relaxed risk for quick hypothesis validation.</summary>
    QuickCheck,
    /// <summary>Moderate costs and standard risk for baseline evaluation.</summary>
    Standard,
    /// <summary>Conservative realism with ATR-scaled slippage and session rules.</summary>
    Realistic,
    /// <summary>Full realism with recommendation for sensitivity and walk-forward studies.</summary>
    ResearchGrade
}

/// <summary>
/// A named, reusable set of configuration defaults for common research scenarios.
/// </summary>
/// <param name="PresetId">Unique identifier for this preset.</param>
/// <param name="Name">Human-readable preset name.</param>
/// <param name="Description">Description of what this preset configures.</param>
/// <param name="Category">The research scenario category.</param>
/// <param name="ExecutionConfig">Execution realism settings applied by this preset.</param>
/// <param name="RiskConfig">Optional risk parameter overrides. Null means no risk overrides.</param>
/// <param name="IsBuiltIn">True for presets shipped with the system; false for user-created presets.</param>
public sealed record ConfigPreset(
    string PresetId,
    string Name,
    string Description,
    PresetCategory Category,
    ExecutionConfig ExecutionConfig,
    RiskConfig? RiskConfig,
    bool IsBuiltIn) : IHasId
{
    /// <inheritdoc/>
    public string Id => PresetId;
}
