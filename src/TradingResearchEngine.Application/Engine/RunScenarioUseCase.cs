using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Metrics;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Engine;

/// <summary>Result wrapper that carries either a <see cref="BacktestResult"/> or validation errors.</summary>
public sealed record ScenarioRunResult(BacktestResult? Result, IReadOnlyList<string>? Errors)
{
    /// <summary>Returns a successful result.</summary>
    public static ScenarioRunResult Success(BacktestResult result) => new(result, null);

    /// <summary>Returns a validation failure with a list of error messages.</summary>
    public static ScenarioRunResult Failure(IReadOnlyList<string> errors) => new(null, errors);

    /// <summary>True when the run completed without validation errors.</summary>
    public bool IsSuccess => Errors is null || Errors.Count == 0;
}

/// <summary>
/// Orchestrates a single backtest run: validates config, resolves components, invokes the engine.
/// </summary>
public sealed class RunScenarioUseCase
{
    private readonly StrategyRegistry _strategyRegistry;
    private readonly IServiceProvider _services;
    private readonly ILogger<RunScenarioUseCase> _logger;
    private readonly IRepository<BacktestResult>? _repository;
    private readonly PreflightValidator _preflightValidator;

    /// <inheritdoc cref="RunScenarioUseCase"/>
    public RunScenarioUseCase(
        StrategyRegistry strategyRegistry,
        IServiceProvider services,
        ILogger<RunScenarioUseCase> logger,
        PreflightValidator preflightValidator)
    {
        _strategyRegistry = strategyRegistry;
        _services = services;
        _logger = logger;
        _preflightValidator = preflightValidator;
        // Optional: auto-save results if repository is registered
        _repository = services.GetService<IRepository<BacktestResult>>();
    }

    /// <summary>
    /// Validates <paramref name="config"/>, resolves all pipeline components, and runs the engine.
    /// Returns a <see cref="ScenarioRunResult"/> with validation errors if config is invalid.
    /// </summary>
    /// <param name="config">Scenario configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="autoSave">When true, persists the result to the repository. Research workflows should pass false.</param>
    public async Task<ScenarioRunResult> RunAsync(ScenarioConfig config, CancellationToken ct = default, bool autoSave = true)
    {
        // V5: Preflight validation replaces the old inline Validate method
        var preflight = _preflightValidator.Validate(config);
        if (preflight.HasErrors)
        {
            var errors = preflight.Findings
                .Where(f => f.Severity == PreflightSeverity.Error)
                .Select(f => f.Message)
                .ToList();
            return ScenarioRunResult.Failure(errors);
        }

        // Resolve strategy type via registry using effective config
        var effectiveStrategy = config.EffectiveStrategyConfig;
        Type strategyType;
        try
        {
            strategyType = _strategyRegistry.Resolve(effectiveStrategy.StrategyType);
        }
        catch (StrategyNotFoundException ex)
        {
            return ScenarioRunResult.Failure(new[] { ex.Message });
        }

        var strategy = CreateStrategy(strategyType, effectiveStrategy.StrategyParameters);
        var effectiveData = config.EffectiveDataConfig;
        var dataProviderFactory = _services.GetRequiredService<IDataProviderFactory>();
        var dataProvider = dataProviderFactory.Create(effectiveData.DataProviderType, effectiveData.DataProviderOptions);
        var riskLayer = _services.GetRequiredService<IRiskLayer>();
        var executionHandler = _services.GetRequiredService<IExecutionHandler>();
        var engineLogger = _services.GetRequiredService<ILogger<BacktestEngine>>();

        // Resolve optional session calendar if configured
        var sessionCalendar = config.SessionOptions?.SessionCalendarType is not null
            ? _services.GetService<Core.Sessions.ISessionCalendar>()
            : null;

        var engine = new BacktestEngine(dataProvider, strategy, riskLayer, executionHandler, engineLogger, sessionCalendar);
        var result = await engine.RunAsync(config, ct);

        // V5: Collect realism advisories from SimulatedExecutionHandler
        if (executionHandler is SimulatedExecutionHandler simHandler && simHandler.RealismAdvisories.Count > 0)
        {
            result = result with { RealismAdvisories = simHandler.RealismAdvisories.ToList().AsReadOnly() };
        }

        // Attach experiment metadata for reproducibility
        var metadata = BuildMetadata(config);
        result = result with { Metadata = metadata };

        // V4: Enrich with trial count and DSR if linked to a strategy version
        result = await EnrichWithTrialCountAndDsrAsync(result, ct);

        // Auto-save result if repository is available and autoSave is enabled
        if (autoSave && _repository is not null && result.Status == BacktestStatus.Completed)
        {
            try { await _repository.SaveAsync(result, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to auto-save BacktestResult {RunId}.", result.RunId); }
        }

        return ScenarioRunResult.Success(result);
    }

    /// <summary>
    /// V4: Increments TotalTrialsRun on the parent StrategyVersion and computes DSR.
    /// Only runs if the result is linked to a strategy version.
    /// </summary>
    private async Task<BacktestResult> EnrichWithTrialCountAndDsrAsync(
        BacktestResult result, CancellationToken ct)
    {
        if (result.StrategyVersionId is null) return result;

        var strategyRepo = _services.GetService<IStrategyRepository>();
        if (strategyRepo is null) return result;

        // Find the version
        StrategyVersion? version = null;
        var strategies = await strategyRepo.ListAsync(ct);
        foreach (var s in strategies)
        {
            var versions = await strategyRepo.GetVersionsAsync(s.StrategyId, ct);
            version = versions.FirstOrDefault(v => v.StrategyVersionId == result.StrategyVersionId);
            if (version is not null) break;
        }
        if (version is null) return result;

        // Increment trial count: completed/failed = +1, cancelled with bars = +1
        bool shouldIncrement = result.Status is BacktestStatus.Completed or BacktestStatus.Failed
            || (result.Status == BacktestStatus.Cancelled && result.EquityCurve.Count > 0);

        if (shouldIncrement)
        {
            var updatedVersion = version with { TotalTrialsRun = version.TotalTrialsRun + 1 };
            try { await strategyRepo.SaveVersionAsync(updatedVersion, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to increment TotalTrialsRun for version {VersionId}.", version.StrategyVersionId); }
            version = updatedVersion;
        }

        // Snapshot trial count
        result = result with { TrialCount = version.TotalTrialsRun };

        // Compute DSR for completed runs with a non-null Sharpe
        if (result.Status == BacktestStatus.Completed && result.SharpeRatio is not null && result.SharpeRatio != 0)
        {
            // Compute skewness and kurtosis from equity curve returns
            var (skewness, kurtosis) = ComputeReturnMoments(result);
            var dsr = DsrCalculator.Compute(
                result.SharpeRatio.Value,
                version.TotalTrialsRun,
                skewness, kurtosis,
                result.EquityCurve.Count,
                result.ScenarioConfig.BarsPerYear);
            result = result with { DeflatedSharpeRatio = dsr };
        }

        return result;
    }

    /// <summary>Computes skewness and excess kurtosis from equity curve period returns.</summary>
    private static (decimal Skewness, decimal Kurtosis) ComputeReturnMoments(BacktestResult result)
    {
        if (result.EquityCurve.Count < 3) return (0m, 0m);

        var returns = new List<double>();
        for (int i = 1; i < result.EquityCurve.Count; i++)
        {
            var prev = (double)result.EquityCurve[i - 1].TotalEquity;
            var curr = (double)result.EquityCurve[i].TotalEquity;
            if (prev > 0) returns.Add(curr / prev - 1.0);
        }

        if (returns.Count < 3) return (0m, 0m);

        double n = returns.Count;
        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / (n - 1);
        double std = Math.Sqrt(variance);
        if (std <= 0) return (0m, 0m);

        double skew = returns.Sum(r => Math.Pow((r - mean) / std, 3)) * n / ((n - 1) * (n - 2));
        double kurt = returns.Sum(r => Math.Pow((r - mean) / std, 4)) * n * (n + 1) / ((n - 1) * (n - 2) * (n - 3))
                      - 3.0 * (n - 1) * (n - 1) / ((n - 2) * (n - 3));

        return ((decimal)Math.Round(skew, 6), (decimal)Math.Round(kurt, 6));
    }

    /// <summary>
    /// Legacy validation method — retained for backward compatibility.
    /// Preflight validation is now handled by <see cref="PreflightValidator"/>.
    /// </summary>
    [Obsolete("Use PreflightValidator.Validate instead. This method is retained for backward compatibility.")]
    private static List<string> Validate(ScenarioConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.ScenarioId)) errors.Add("ScenarioId is required.");
        if (string.IsNullOrWhiteSpace(config.StrategyType)) errors.Add("StrategyType is required.");
        if (string.IsNullOrWhiteSpace(config.DataProviderType)) errors.Add("DataProviderType is required.");
        if (config.InitialCash <= 0) errors.Add("InitialCash must be greater than zero.");
        if (config.AnnualRiskFreeRate < 0) errors.Add("AnnualRiskFreeRate must be non-negative.");
        if (config.BarsPerYear <= 0) errors.Add("BarsPerYear must be greater than zero.");
        return errors;
    }

    private IStrategy CreateStrategy(Type strategyType, Dictionary<string, object> parameters)
    {
        // Try to match constructor parameters by name from the StrategyParameters dictionary
        var ctors = strategyType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .ToArray();

        foreach (var ctor in ctors)
        {
            var ctorParams = ctor.GetParameters();
            var args = new object?[ctorParams.Length];
            bool allResolved = true;

            for (int i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                // Try case-insensitive match from StrategyParameters
                var match = parameters.FirstOrDefault(kv =>
                    string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase));

                if (match.Key is not null)
                {
                    try
                    {
                        var rawValue = match.Value;
                        // Handle System.Text.Json's JsonElement
                        if (rawValue is System.Text.Json.JsonElement je)
                            rawValue = ConvertJsonElement(je, p.ParameterType);
                        args[i] = Convert.ChangeType(rawValue, p.ParameterType);
                        continue;
                    }
                    catch { /* fall through to default */ }
                }

                if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }

                allResolved = false;
                break;
            }

            if (allResolved)
                return (IStrategy)ctor.Invoke(args);
        }

        // Fallback: parameterless or DI-resolved
        return (IStrategy)ActivatorUtilities.CreateInstance(_services, strategyType);
    }

    private static object? ConvertJsonElement(System.Text.Json.JsonElement je, Type targetType)
    {
        if (targetType == typeof(int)) return je.GetInt32();
        if (targetType == typeof(decimal)) return je.GetDecimal();
        if (targetType == typeof(double)) return je.GetDouble();
        if (targetType == typeof(bool)) return je.ValueKind == System.Text.Json.JsonValueKind.True
            || (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.GetInt32() != 0);
        if (targetType == typeof(string)) return je.GetString();
        if (targetType == typeof(long)) return je.GetInt64();
        if (targetType == typeof(float)) return je.GetSingle();
        return je.ToString();
    }

    private static Core.Results.ExperimentMetadata BuildMetadata(ScenarioConfig config)
    {
        var dataOpts = config.DataProviderOptions;
        var from = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df ? df : DateTimeOffset.MinValue;
        var to = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt ? dt : DateTimeOffset.MaxValue;

        return new Core.Results.ExperimentMetadata(
            config.StrategyType,
            new Dictionary<string, object>(config.StrategyParameters),
            config.DataProviderType,
            from, to,
            config.RealismProfile,
            config.SlippageModelType,
            config.ExecutionOptions?.SlippageModelOptions,
            config.CommissionModelType,
            config.EffectiveFillMode,
            config.BarsPerYear,
            config.RandomSeed,
            null); // EngineVersion populated at composition root if available
    }
}
