using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// A research workflow that runs one or more engine instances
/// to produce comparative or statistical results.
/// </summary>
/// <typeparam name="TOptions">Options controlling the workflow.</typeparam>
/// <typeparam name="TResult">Result type produced by the workflow.</typeparam>
public interface IResearchWorkflow<in TOptions, TResult>
{
    /// <summary>Executes the workflow.</summary>
    Task<TResult> RunAsync(
        ScenarioConfig baseConfig,
        TOptions options,
        CancellationToken ct = default);

    /// <summary>Executes the workflow with progress reporting.</summary>
    Task<TResult> RunAsync(
        ScenarioConfig baseConfig,
        TOptions options,
        IProgress<ProgressUpdate>? progress,
        CancellationToken ct = default)
        => RunAsync(baseConfig, options, ct); // default: ignore progress
}
