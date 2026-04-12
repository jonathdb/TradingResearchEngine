namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Identifies the kind of async execution a <see cref="BacktestJob"/> represents.
/// </summary>
public enum JobType
{
    /// <summary>A single backtest run.</summary>
    SingleRun,

    /// <summary>Monte Carlo simulation study.</summary>
    MonteCarlo,

    /// <summary>Walk-forward analysis study.</summary>
    WalkForward,

    /// <summary>Parameter sweep / grid search study.</summary>
    ParameterSweep,

    /// <summary>Sensitivity analysis study.</summary>
    Sensitivity,

    /// <summary>Parameter stability study.</summary>
    Stability,

    /// <summary>Realism sensitivity study.</summary>
    Realism,

    /// <summary>Parameter perturbation study.</summary>
    Perturbation,

    /// <summary>Regime segmentation study.</summary>
    RegimeSegmentation,

    /// <summary>Benchmark comparison study.</summary>
    BenchmarkComparison
}
