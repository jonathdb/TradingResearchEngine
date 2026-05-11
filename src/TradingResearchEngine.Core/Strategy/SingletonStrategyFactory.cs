using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// Adapter that wraps a pre-built <see cref="IStrategy"/> instance as an <see cref="IStrategyFactory"/>.
/// Used for backward compatibility when callers provide a strategy instance directly.
/// </summary>
/// <remarks>
/// This factory always returns the same instance — it does NOT provide isolation.
/// For parallel workflows, use a proper <see cref="IStrategyFactory"/> implementation
/// that creates new instances on each <see cref="Create"/> call.
/// </remarks>
internal sealed class SingletonStrategyFactory : IStrategyFactory
{
    private readonly IStrategy _strategy;

    /// <summary>Wraps an existing strategy instance.</summary>
    public SingletonStrategyFactory(IStrategy strategy)
    {
        _strategy = strategy;
    }

    /// <inheritdoc/>
    public string StrategyType => _strategy.GetType().Name;

    /// <inheritdoc/>
    /// <remarks>Always returns the same instance. Not suitable for parallel workflows.</remarks>
    public IStrategy Create(StrategyConfig config) => _strategy;
}
