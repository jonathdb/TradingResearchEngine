using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Core.Engine;

/// <summary>Runs a single backtest simulation from a <see cref="ScenarioConfig"/>.</summary>
public interface IBacktestEngine
{
    /// <summary>Executes the simulation and returns a structured result.</summary>
    /// <param name="config">Scenario configuration controlling the backtest.</param>
    /// <param name="progress">Optional progress reporter. When non-null, receives ~100 updates per run.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    Task<BacktestResult> RunAsync(
        ScenarioConfig config,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken ct = default);
}
