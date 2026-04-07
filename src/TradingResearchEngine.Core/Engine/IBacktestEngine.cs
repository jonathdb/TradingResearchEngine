using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Core.Engine;

/// <summary>Runs a single backtest simulation from a <see cref="ScenarioConfig"/>.</summary>
public interface IBacktestEngine
{
    /// <summary>Executes the simulation and returns a structured result.</summary>
    Task<BacktestResult> RunAsync(ScenarioConfig config, CancellationToken ct = default);
}
