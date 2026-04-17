namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Execution realism settings sub-object for <see cref="ScenarioConfig"/> decomposition.
/// Groups slippage, commission, fill mode, realism profile, and session options.
/// </summary>
public sealed record ExecutionConfig(
    /// <summary>Slippage model implementation key.</summary>
    string SlippageModelType = "ZeroSlippageModel",
    /// <summary>Commission model implementation key.</summary>
    string CommissionModelType = "ZeroCommissionModel",
    /// <summary>When approved orders are filled relative to the generating bar.</summary>
    FillMode FillMode = FillMode.NextBarOpen,
    /// <summary>Execution realism level controlling default slippage/fill behaviour.</summary>
    ExecutionRealismProfile RealismProfile = ExecutionRealismProfile.StandardBacktest,
    /// <summary>Fine-grained overrides for individual realism profile defaults.</summary>
    ExecutionOptions? ExecutionOptions = null,
    /// <summary>Session filtering and calendar configuration.</summary>
    SessionOptions? SessionOptions = null,
    /// <summary>V6: When true, a Short signal while Long (or vice versa) closes then opens. Default false.</summary>
    bool AllowReversals = false);
