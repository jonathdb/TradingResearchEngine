using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Resolves <see cref="IStrategyFactory"/> instances by strategy type name.
/// Used by parallel workflows to obtain factories that create isolated strategy instances.
/// </summary>
public interface IStrategyFactoryProvider
{
    /// <summary>
    /// Returns the <see cref="IStrategyFactory"/> for the given strategy type name.
    /// Throws <see cref="StrategyNotFoundException"/> when the strategy type is not registered.
    /// </summary>
    IStrategyFactory GetFactory(string strategyType);
}
