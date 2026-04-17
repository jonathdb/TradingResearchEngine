namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Configuration options for Combinatorial Purged Cross-Validation (CPCV).
/// </summary>
public sealed record CpcvOptions(
    /// <summary>Total number of folds (paths). Must be ≥ 3.</summary>
    int NumPaths = 6,
    /// <summary>Number of folds held out for testing per combination. Must be ≥ 1 and &lt; NumPaths.</summary>
    int TestFolds = 2,
    /// <summary>Optional seed for deterministic output.</summary>
    int? Seed = null);
