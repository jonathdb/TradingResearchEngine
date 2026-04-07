namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Decorates an <see cref="Core.Strategy.IStrategy"/> implementation with a unique
/// lowercase-kebab-case name used by <see cref="StrategyRegistry"/> for resolution.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StrategyNameAttribute : Attribute
{
    /// <summary>The unique kebab-case name for this strategy.</summary>
    public string Name { get; }

    /// <param name="name">Lowercase kebab-case identifier (e.g. "moving-average-crossover").</param>
    public StrategyNameAttribute(string name) => Name = name;
}
