namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Lightweight metadata for a built-in strategy, surfaced in the Builder
/// and Strategy Detail UX. Lookup is by <see cref="StrategyType"/> string match
/// against <see cref="StrategyTemplate.StrategyType"/>.
/// Missing descriptor is non-fatal — UI falls back to raw type name.
/// </summary>
public sealed record StrategyDescriptor(
    string StrategyType,
    string DisplayName,
    string Family,
    string Description,
    string Hypothesis,
    string? BestFor = null,
    string[]? SuggestedStudies = null,
    /// <summary>V5: Parameter schema list, populated lazily from <see cref="IStrategySchemaProvider"/>.</summary>
    IReadOnlyList<StrategyParameterSchema>? ParameterSchemas = null);
