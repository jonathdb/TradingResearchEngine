using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <inheritdoc cref="RunScenarioUseCase"/>
    public RunScenarioUseCase(
        StrategyRegistry strategyRegistry,
        IServiceProvider services,
        ILogger<RunScenarioUseCase> logger)
    {
        _strategyRegistry = strategyRegistry;
        _services = services;
        _logger = logger;
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
        var errors = Validate(config);
        if (errors.Count > 0) return ScenarioRunResult.Failure(errors);

        // Resolve strategy type via registry
        Type strategyType;
        try
        {
            strategyType = _strategyRegistry.Resolve(config.StrategyType);
        }
        catch (StrategyNotFoundException ex)
        {
            return ScenarioRunResult.Failure(new[] { ex.Message });
        }

        var strategy = CreateStrategy(strategyType, config.StrategyParameters);
        var dataProviderFactory = _services.GetRequiredService<IDataProviderFactory>();
        var dataProvider = dataProviderFactory.Create(config.DataProviderType, config.DataProviderOptions);
        var riskLayer = _services.GetRequiredService<IRiskLayer>();
        var executionHandler = _services.GetRequiredService<IExecutionHandler>();
        var engineLogger = _services.GetRequiredService<ILogger<BacktestEngine>>();

        // Resolve optional session calendar if configured
        var sessionCalendar = config.SessionOptions?.SessionCalendarType is not null
            ? _services.GetService<Core.Sessions.ISessionCalendar>()
            : null;

        var engine = new BacktestEngine(dataProvider, strategy, riskLayer, executionHandler, engineLogger, sessionCalendar);
        var result = await engine.RunAsync(config, ct);

        // Attach experiment metadata for reproducibility
        var metadata = BuildMetadata(config);
        result = result with { Metadata = metadata };

        // Auto-save result if repository is available and autoSave is enabled
        if (autoSave && _repository is not null && result.Status == BacktestStatus.Completed)
        {
            try { await _repository.SaveAsync(result, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to auto-save BacktestResult {RunId}.", result.RunId); }
        }

        return ScenarioRunResult.Success(result);
    }

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
