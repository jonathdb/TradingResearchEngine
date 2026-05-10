using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// Creates isolated <see cref="IStrategy"/> instances for use in parallel workflows.
/// Each call to <see cref="Create"/> MUST return a new, independent instance with its own state.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe: <see cref="Create"/> may be called concurrently
/// from multiple threads in walk-forward and parameter sweep workflows.
/// </remarks>
public interface IStrategyFactory
{
    /// <summary>The strategy type name this factory produces (matches <see cref="StrategyConfig.StrategyType"/>).</summary>
    string StrategyType { get; }

    /// <summary>
    /// Creates a new independent strategy instance configured with the given parameters.
    /// Each invocation returns a fresh instance with no shared mutable state.
    /// </summary>
    IStrategy Create(StrategyConfig config);
}
