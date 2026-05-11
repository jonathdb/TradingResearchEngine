using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;

namespace TradingResearchEngine.Application.AI;

/// <summary>
/// Immutable record representing a machine-generated strategy configuration
/// produced by the AI Strategy Assistant. Contains all fields needed to
/// populate the strategy builder form and provide user-facing context.
/// </summary>
/// <param name="StrategyName">Human-readable name for the generated strategy.</param>
/// <param name="Hypothesis">The market hypothesis the strategy is designed to exploit.</param>
/// <param name="StrategyType">Strategy type name matching a <c>StrategyRegistry.KnownNames</c> entry.</param>
/// <param name="Parameters">Strategy-specific parameter key-value pairs.</param>
/// <param name="SuggestedRisk">Suggested risk configuration for the strategy.</param>
/// <param name="Rationale">Explanation of why the AI chose this configuration.</param>
/// <param name="Caveats">Warnings or limitations the user should be aware of.</param>
/// <param name="CompositeConfig">
/// Optional composite strategy configuration. Non-null when <paramref name="StrategyType"/> is
/// <c>"composite"</c>; must be null for compiled strategy types.
/// </param>
/// <param name="SourceType">Provenance tag; defaults to <see cref="Strategy.SourceType.AIGenerated"/>.</param>
/// <param name="RefinementHistory">History of refinement prompts applied to this draft.</param>
public sealed record AIStrategyDraft(
    string StrategyName,
    string Hypothesis,
    string StrategyType,
    IReadOnlyDictionary<string, object> Parameters,
    RiskConfig SuggestedRisk,
    string Rationale,
    IReadOnlyList<string> Caveats,
    CompositeStrategyConfig? CompositeConfig = null,
    SourceType SourceType = SourceType.AIGenerated,
    IReadOnlyList<string> RefinementHistory = null!)
{
    /// <summary>History of refinement prompts applied to this draft.</summary>
    public IReadOnlyList<string> RefinementHistory { get; init; } = RefinementHistory ?? Array.Empty<string>();
}
