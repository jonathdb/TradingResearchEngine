namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Thrown by <see cref="StrategyRegistry"/> when a strategy name cannot be resolved.
/// Treated as a validation error by <c>RunScenarioUseCase</c> — no engine run is started.
/// </summary>
public sealed class StrategyNotFoundException : Exception
{
    /// <summary>The name that was requested but not found.</summary>
    public string RequestedName { get; }

    /// <summary>All strategy names currently registered.</summary>
    public IReadOnlyList<string> KnownNames { get; }

    /// <inheritdoc cref="StrategyNotFoundException"/>
    public StrategyNotFoundException(string requestedName, IReadOnlyList<string> knownNames)
        : base($"Strategy '{requestedName}' not found. Known strategies: {string.Join(", ", knownNames)}")
    {
        RequestedName = requestedName;
        KnownNames = knownNames;
    }
}
