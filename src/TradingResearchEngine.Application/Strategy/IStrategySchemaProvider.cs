namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Returns typed parameter schemas for registered strategies.
/// Consumed by the builder, API discovery endpoints, and CLI.
/// </summary>
public interface IStrategySchemaProvider
{
    /// <summary>
    /// Returns the parameter schema for the named strategy.
    /// Never returns an empty list for a registered strategy — falls back
    /// to constructor inspection when <see cref="ParameterMetaAttribute"/> is absent.
    /// </summary>
    IReadOnlyList<StrategyParameterSchema> GetSchema(string strategyName);
}
