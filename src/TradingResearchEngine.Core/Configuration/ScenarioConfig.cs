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
    string? Timeframe = null,
    /// <summary>V5: Data provider settings sub-object. When present, takes precedence over top-level data fields.</summary>
    DataConfig? Data = null,
    /// <summary>V5: Strategy type and parameters sub-object. When present, takes precedence over top-level strategy fields.</summary>
    StrategyConfig? Strategy = null,
    /// <summary>V5: Risk parameters sub-object. When present, takes precedence over top-level risk fields.</summary>
    RiskConfig? Risk = null,
    /// <summary>V5: Execution realism sub-object. When present, takes precedence over top-level execution fields.</summary>
    ExecutionConfig? Execution = null,
    /// <summary>V5: Research workflow sub-object. When present, takes precedence over top-level research fields.</summary>
    ResearchConfig? Research = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => ScenarioId;

    /// <summary>Effective data config: sub-object wins, falls back to top-level fields.</summary>
    public DataConfig EffectiveDataConfig => Data ?? new DataConfig(
        DataProviderType, DataProviderOptions, Timeframe, BarsPerYear);

    /// <summary>Effective strategy config: sub-object wins, falls back to top-level fields.</summary>
    public StrategyConfig EffectiveStrategyConfig => Strategy ?? new StrategyConfig(
        StrategyType, StrategyParameters);

    /// <summary>Effective risk config: sub-object wins, falls back to top-level fields.</summary>
    public RiskConfig EffectiveRiskConfig => Risk ?? new RiskConfig(
        RiskParameters, InitialCash, AnnualRiskFreeRate);

    /// <summary>Effective execution config: sub-object wins, falls back to top-level fields.</summary>
    public ExecutionConfig EffectiveExecutionConfig => Execution ?? new ExecutionConfig(
        SlippageModelType, CommissionModelType, FillMode, RealismProfile,
        ExecutionOptions, SessionOptions);

    /// <summary>Effective research config: sub-object wins, falls back to top-level fields.</summary>
    public ResearchConfig EffectiveResearchConfig => Research ?? new ResearchConfig(
        ResearchWorkflowType, ResearchWorkflowOptions, RandomSeed, TraceOptions);

    /// <summary>Effective fill mode: ExecutionOptions override → sub-object → top-level FillMode.</summary>
    public FillMode EffectiveFillMode =>
        EffectiveExecutionConfig.ExecutionOptions?.FillModeOverride
        ?? EffectiveExecutionConfig.FillMode;

    /// <summary>Whether event tracing is enabled.</summary>
    public bool EnableEventTrace =>
        EffectiveResearchConfig.TraceOptions?.EnableEventTrace ?? false;
}
