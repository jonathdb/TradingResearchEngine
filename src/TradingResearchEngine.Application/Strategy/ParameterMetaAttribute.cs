namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Annotates a strategy constructor parameter with rich metadata for the builder,
/// API discovery, and CLI. When absent, the schema provider falls back to
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
}
