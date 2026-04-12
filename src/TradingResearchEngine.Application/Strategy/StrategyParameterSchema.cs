namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Typed parameter descriptor for a strategy constructor parameter.
/// Consumed by the builder, API discovery endpoints, and CLI to render
/// informed parameter editors without hard-coding knowledge of each strategy.
/// </summary>
public sealed record StrategyParameterSchema(
    /// <summary>Constructor parameter name (camelCase).</summary>
    string Name,
    /// <summary>Human-readable display name.</summary>
    string DisplayName,
    /// <summary>Parameter type: "int", "decimal", "bool", or "enum".</summary>
    string Type,
    /// <summary>Default value from the constructor or attribute.</summary>
    object DefaultValue,
    /// <summary>Whether the parameter is required (no default value).</summary>
    bool IsRequired,
    /// <summary>Minimum allowed value, if applicable.</summary>
    object? Min,
    /// <summary>Maximum allowed value, if applicable.</summary>
    object? Max,
    /// <summary>Valid choices for enum-typed parameters.</summary>
    string[]? EnumChoices,
    /// <summary>Help text describing what the parameter does.</summary>
    string Description,
    /// <summary>Overfitting sensitivity classification.</summary>
    SensitivityHint SensitivityHint,
    /// <summary>Logical group: Signal, Entry, Exit, Risk, Filters, or Execution.</summary>
    string Group,
    /// <summary>Whether this parameter is hidden in Simple mode.</summary>
    bool IsAdvanced,
    /// <summary>Display order within its group.</summary>
    int DisplayOrder);
