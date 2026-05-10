using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Resolves <see cref="IStrategyFactory"/> instances by looking up the strategy type
/// in the <see cref="StrategyRegistry"/> and wrapping it in a <see cref="ReflectionStrategyFactory"/>.
/// </summary>
public sealed class StrategyFactoryProvider : IStrategyFactoryProvider
{
    private readonly StrategyRegistry _registry;
    private readonly IServiceProvider _services;

    /// <inheritdoc cref="StrategyFactoryProvider"/>
    public StrategyFactoryProvider(StrategyRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    /// <inheritdoc/>
    public IStrategyFactory GetFactory(string strategyType)
    {
        var type = _registry.Resolve(strategyType);
        return new ReflectionStrategyFactory(strategyType, type, _services);
    }
}
