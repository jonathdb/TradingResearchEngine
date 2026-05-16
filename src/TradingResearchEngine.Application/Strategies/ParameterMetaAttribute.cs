namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Annotates a strategy constructor parameter with rich metadata for the strategy
/// builder and parameter sweep UI. When absent, the schema provider falls back to
/// constructor parameter name, inferred type, and default value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterMetaAttribute : Attribute
{
    /// <summary>Human-readable display name. Falls back to formatted parameter name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Help text describing what the parameter does.</summary>
    public string? Description { get; set; }

    /// <summary>Overfitting sensitivity classification. Default: Medium.</summary>
    public SensitivityHint SensitivityHint { get; set; } = SensitivityHint.Medium;

    /// <summary>Logical group: Signal, Entry, Exit, Risk, Filters, or Execution.</summary>
    public string Group { get; set; } = "Signal";

    /// <summary>Whether this parameter is hidden in Simple mode.</summary>
    public bool IsAdvanced { get; set; }

    /// <summary>Display order within its group.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Minimum allowed value (numeric parameters only).</summary>
    public object? Min { get; set; }

    /// <summary>Maximum allowed value (numeric parameters only).</summary>
    public object? Max { get; set; }

    /// <summary>
    /// Schema-driven default value for this parameter. When set, this value is used
    /// instead of runtime type-based inference (e.g., <c>typeof(int) → 0</c>).
    /// Takes precedence over type-based defaults but is overridden by the C# constructor
    /// default value when present.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// Indicates whether <see cref="Default"/> was explicitly set.
    /// Required because <c>null</c> may be a valid explicit default.
    /// </summary>
    public bool HasDefault { get; set; }
}
