using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Core.Results;

/// <summary>
/// Metadata sufficient to reproduce a backtest result exactly.
/// Attached to <see cref="BacktestResult"/> for auditability.
/// </summary>
public sealed record ExperimentMetadata(
    string StrategyName,
    Dictionary<string, object> ParameterValues,
    string DataSourceIdentifier,
    DateTimeOffset DataRangeStart,
    DateTimeOffset DataRangeEnd,
    ExecutionRealismProfile RealismProfile,
    string SlippageModelType,
    Dictionary<string, object>? SlippageModelOptions,
    string CommissionModelType,
    FillMode FillMode,
    int BarsPerYear,
    int? RandomSeed,
    string? EngineVersion,
    /// <summary>V5: Preset ID used for this run, if any.</summary>
    string? PresetId = null,
    /// <summary>V5: Data file hash or last-modified timestamp for reproducibility.</summary>
    string? DataFileIdentity = null);
