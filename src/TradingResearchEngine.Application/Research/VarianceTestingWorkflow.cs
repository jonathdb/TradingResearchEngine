using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>Options for variance testing — may include a user-defined preset.</summary>
public sealed class VarianceOptions
{
    /// <summary>Optional user-defined preset overrides as a dictionary.</summary>
    public Dictionary<string, object>? UserDefinedPreset { get; set; }
}

/// <summary>
/// Runs Conservative, Base, and Strong variance presets (plus optional user-defined)
/// to stress-test a strategy across a range of market assumptions.
/// </summary>
public sealed class VarianceTestingWorkflow : IResearchWorkflow<VarianceOptions, VarianceResult>
{
    private readonly RunScenarioUseCase _runScenario;

    /// <inheritdoc cref="VarianceTestingWorkflow"/>
    public VarianceTestingWorkflow(RunScenarioUseCase runScenario) => _runScenario = runScenario;

    /// <inheritdoc/>
    public async Task<VarianceResult> RunAsync(ScenarioConfig baseConfig, VarianceOptions options, CancellationToken ct = default)
    {
        var presets = new List<(string Name, Dictionary<string, object> Overrides)>
        {
            ("Conservative", new Dictionary<string, object>
            {
                ["SlippageMultiplier"] = 2.0m,
                ["CommissionMultiplier"] = 1.5m,
                ["WinRateAdjustment"] = -0.05m,
            }),
            ("Base", new Dictionary<string, object>()),
            ("Strong", new Dictionary<string, object>
            {
                ["SlippageMultiplier"] = 0.5m,
                ["CommissionMultiplier"] = 0.75m,
                ["WinRateAdjustment"] = 0.03m,
            }),
        };

        if (options.UserDefinedPreset is not null)
            presets.Add(("UserDefined", options.UserDefinedPreset));

        var variants = new List<(string PresetName, BacktestResult Result)>();

        foreach (var (name, overrides) in presets)
        {
            var merged = MergeOverrides(baseConfig, overrides);
            var runResult = await _runScenario.RunAsync(merged, ct, autoSave: false);
            if (runResult.IsSuccess && runResult.Result is not null)
                variants.Add((name, runResult.Result));
        }

        return new VarianceResult(variants);
    }

    private static ScenarioConfig MergeOverrides(ScenarioConfig baseConfig, Dictionary<string, object> overrides)
    {
        if (overrides.Count == 0) return baseConfig;

        var merged = new Dictionary<string, object>(baseConfig.StrategyParameters);
        foreach (var (key, value) in overrides)
            merged[key] = value;

        return baseConfig with { StrategyParameters = merged };
    }
}
