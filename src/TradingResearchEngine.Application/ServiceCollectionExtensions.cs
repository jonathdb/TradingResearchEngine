using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Application.Portfolio;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application;

/// <summary>DI registration extensions for the Application layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Core and Application services.
    /// Call <see cref="AddStrategyAssembly"/> afterwards to register strategy assemblies.
    /// </summary>
    public static IServiceCollection AddTradingResearchEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RiskOptions>(configuration.GetSection("Risk"));
        services.Configure<MonteCarloOptions>(configuration.GetSection("MonteCarlo"));
        services.Configure<ReportingOptions>(configuration.GetSection("Reporting"));
        services.Configure<RepositoryOptions>(configuration.GetSection("Repository"));
        services.Configure<SweepOptions>(configuration.GetSection("Sweep"));
        services.Configure<WalkForwardOptions>(configuration.GetSection("WalkForward"));
        services.Configure<ConcurrencyOptions>(configuration.GetSection("Concurrency"));

        // Typed data provider configuration — bound from appsettings.json:DataProviders:{Provider}
        services.Configure<CsvDataProviderOptions>(configuration.GetSection("DataProviders:Csv"));
        services.Configure<HttpDataProviderOptions>(configuration.GetSection("DataProviders:Http"));
        services.Configure<DukascopyDataProviderOptions>(configuration.GetSection("DataProviders:Dukascopy"));

        // Global concurrency budget — singleton shared across all parallel workflows
        services.AddSingleton<ConcurrencyBudget>();

        // V8: Bind GeminiOptions from configuration
        services.Configure<GeminiOptions>(configuration.GetSection("Gemini"));

        services.AddSingleton<StrategyRegistry>(sp =>
        {
            var registry = new StrategyRegistry();
            var opts = sp.GetRequiredService<IOptions<StrategyRegistryOptions>>().Value;
            foreach (var asm in opts.Assemblies)
                registry.RegisterAssembly(asm);
            return registry;
        });
        services.Configure<StrategyRegistryOptions>(_ => { }); // ensure options exist

        // IStrategyFactory: provides isolated strategy instances for parallel workflows
        services.AddSingleton<IStrategyFactoryProvider>(sp =>
        {
            var registry = sp.GetRequiredService<StrategyRegistry>();
            return new StrategyFactoryProvider(registry, sp);
        });
        services.AddScoped<RunScenarioUseCase>();
        // BacktestEngine is created via IBacktestEngineFactory — not registered directly in DI
        services.AddTransient<IBacktestEngineFactory, BacktestEngineFactory>();
        services.AddTransient<IRiskLayer, DefaultRiskLayer>();

        // Default position sizing policy — can be overridden via DI
        services.AddTransient<Core.Risk.IPositionSizingPolicy, PercentEquitySizingPolicy>();

        // Default fallback models — overridden by Infrastructure registrations if present
        services.AddTransient<ISlippageModel, ZeroSlippageModel>();
        services.AddTransient<ICommissionModel, ZeroCommissionModel>();
        services.AddTransient<IExecutionHandler, SimulatedExecutionHandler>();

        // Research workflows
        services.AddScoped<ParameterSweepWorkflow>();
        services.AddScoped<MonteCarloWorkflow>();
        services.AddScoped<WalkForwardWorkflow>();
        services.AddScoped<VarianceTestingWorkflow>();
        services.AddScoped<ScenarioComparisonUseCase>();
        services.AddScoped<ParameterPerturbationWorkflow>();
        services.AddScoped<RandomizedOosWorkflow>();

        // Benchmark comparison
        services.AddScoped<BenchmarkComparisonWorkflow>();

        // Realism sensitivity
        services.AddScoped<RealismSensitivityWorkflow>();

        // V6: CPCV study handler
        services.AddScoped<CpcvStudyHandler>();

        // Prop-firm module
        services.AddScoped<PropFirmEvaluator>();
        services.AddScoped<PropFirmVarianceWorkflow>();

        // V4: Research checklist and final validation
        services.AddScoped<ResearchChecklistService>();

        // Study interpretation service — generates result-aware textual interpretations
        services.AddScoped<StudyInterpretationService>();
        services.AddScoped<FinalValidationUseCase>();

        // V4: Sealed test set guard with audit logging
        services.AddScoped<SealedTestSetGuard>();
        services.AddScoped<ITestSetGuard, StrategyVersionTestSetGuard>();

        // V5: Preflight validation and resolved config
        services.AddScoped<PreflightValidator>();
        services.AddScoped<ResolvedConfigService>();
        services.AddSingleton<IStrategySchemaProvider, StrategySchemaProvider>();

        // V4: Background study service (singleton — manages study lifecycle across navigations)
        services.AddSingleton<BackgroundStudyService>();

        // Job executor (singleton — manages async job lifecycle and cleanup)
        services.AddSingleton<JobExecutor>();

        // V8: Portfolio backtest runner
        services.AddScoped<PortfolioBacktestRunner>();

        // V8: Result export service for browser file downloads
        services.AddSingleton<IResultExportService, ResultExportService>();

        // V8: BarDataPool singleton for hot-path allocation reduction
        services.AddSingleton<BarDataPool>();

        // Register all Skender catalog indicators in the Core IndicatorRegistry
        SkenderIndicatorCatalog.RegisterInIndicatorRegistry();

        // Startup validation: iterates all catalog entries and logs warnings for failures
        services.AddHostedService<IndicatorCatalogValidationService>();

        // Startup verification: attempts instantiation of all registered strategies
        services.AddHostedService<StrategyRegistryValidationService>();

        // V8: AI Strategy Assistant — conditionally registered based on API key availability
        services.AddSingleton<IAIStrategyAssistant>(sp =>
        {
            var geminiOptions = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<DisabledAIStrategyAssistant>>();

            if (string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
            {
                logger.LogWarning("Gemini API key is not configured. AI strategy assistant features are disabled.");
                return new DisabledAIStrategyAssistant();
            }

            // Actual implementation is registered by Infrastructure layer
            // This fallback ensures graceful degradation when Infrastructure hasn't registered one
            return new DisabledAIStrategyAssistant();
        });

        return services;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for <see cref="Core.Strategy.IStrategy"/> implementations
    /// decorated with <see cref="StrategyNameAttribute"/> and registers them with the singleton
    /// <see cref="StrategyRegistry"/>.
    /// </summary>
    public static IServiceCollection AddStrategyAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        // Defer registration to after the container is built by storing assemblies
        // and scanning them when StrategyRegistry is first resolved.
        services.Configure<StrategyRegistryOptions>(opts => opts.Assemblies.Add(assembly));
        return services;
    }
}

/// <summary>Holds assemblies to scan for strategies at startup.</summary>
public sealed class StrategyRegistryOptions
{
    /// <summary>Assemblies to scan for IStrategy implementations.</summary>
    public List<Assembly> Assemblies { get; } = new();
}

/// <summary>
/// No-op AI strategy assistant used when the Gemini API key is not configured.
/// Throws descriptive errors when invoked, allowing the application to start without crashing.
/// </summary>
internal sealed class DisabledAIStrategyAssistant : IAIStrategyAssistant
{
    public Task<AIStrategyDraft> GenerateStrategyAsync(string naturalLanguagePrompt, CancellationToken ct)
        => throw new InvalidOperationException(
            "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");

    public Task<AIStrategyDraft> RefineStrategyAsync(
        AIStrategyDraft current, Core.Results.BacktestResult lastResult,
        string refinementPrompt, CancellationToken ct)
        => throw new InvalidOperationException(
            "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");

    public IAsyncEnumerable<string> StreamGenerateAsync(
        string prompt, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");
    }

    public IAsyncEnumerable<string> StreamRefineAsync(
        AIStrategyDraft current, string refinementPrompt,
        CancellationToken ct)
    {
        throw new InvalidOperationException(
            "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");
    }
}
