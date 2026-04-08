namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Overrides individual execution realism profile defaults.
/// Any non-null field takes precedence over the profile's default.
/// </summary>
public sealed record ExecutionOptions(
    FillMode? FillModeOverride = null,
    string? SlippageModelOverride = null,
    Dictionary<string, object>? SlippageModelOptions = null,
    bool? EnablePartialFills = null,
    int? DefaultMaxBarsPending = null);

/// <summary>
/// Session filtering and calendar configuration.
/// </summary>
public sealed record SessionOptions(
    string? SessionCalendarType = null,
    Dictionary<string, object>? SessionFilterOptions = null);

/// <summary>
/// Trace and debugging options.
/// </summary>
public sealed record TraceOptions(
    bool EnableEventTrace = false);
