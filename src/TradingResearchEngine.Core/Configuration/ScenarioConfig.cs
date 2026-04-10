using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// The sole input required to initialise and execute a simulation run.
/// Deserialised from a JSON file (CLI) or request body (API).
/// Implements <see cref="IHasId"/> for persistence via <c>IRepository&lt;ScenarioConfig&gt;</c>.
/// </summary>
public sealed record ScenarioConfig(
    string ScenarioId,
    string Description,
    ReplayMode ReplayMode,
    string DataProviderType,
    Dictionary<string, object> DataProviderOptions,
    string StrategyType,
    Dictionary<string, object> StrategyParameters,
    Dictionary<string, object> RiskParameters,
    string SlippageModelType,
    string CommissionModelType,
    decimal InitialCash,
    decimal AnnualRiskFreeRate,
    int? RandomSeed,
    string? ResearchWorkflowType,
    Dictionary<string, object>? ResearchWorkflowOptions,
    PropFirmOptions? PropFirmOptions,
    FillMode FillMode = FillMode.NextBarOpen,
    int BarsPerYear = 252,
    ExecutionRealismProfile RealismProfile = ExecutionRealismProfile.StandardBacktest,
    ExecutionOptions? ExecutionOptions = null,
    SessionOptions? SessionOptions = null,
    TraceOptions? TraceOptions = null,
    /// <summary>V4: Explicit timeframe label (e.g. "Daily", "H4"). Null for legacy configs.</summary>
    string? Timeframe = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => ScenarioId;

    /// <summary>Effective fill mode: ExecutionOptions override takes precedence over top-level FillMode.</summary>
    public FillMode EffectiveFillMode =>
        ExecutionOptions?.FillModeOverride ?? FillMode;

    /// <summary>Whether event tracing is enabled.</summary>
    public bool EnableEventTrace =>
        TraceOptions?.EnableEventTrace ?? false;
}
