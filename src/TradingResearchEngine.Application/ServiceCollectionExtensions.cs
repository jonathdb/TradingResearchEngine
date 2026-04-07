using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Application.Strategy;
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

        services.AddSingleton<StrategyRegistry>(sp =>
        {
            var registry = new StrategyRegistry();
            var opts = sp.GetRequiredService<IOptions<StrategyRegistryOptions>>().Value;
            foreach (var asm in opts.Assemblies)
                registry.RegisterAssembly(asm);
            return registry;
        });
        services.Configure<StrategyRegistryOptions>(_ => { }); // ensure options exist
        services.AddScoped<RunScenarioUseCase>();
        // BacktestEngine is constructed manually by RunScenarioUseCase — not registered in DI
        services.AddTransient<IRiskLayer, DefaultRiskLayer>();

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

        // Prop-firm module
        services.AddScoped<PropFirmEvaluator>();
        services.AddScoped<PropFirmVarianceWorkflow>();

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
