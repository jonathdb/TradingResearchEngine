namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// The research lifecycle stage of a strategy. Progresses from initial
/// hypothesis through validation to a final held-out test.
/// </summary>
public enum DevelopmentStage
{
    /// <summary>Idea only, no runs yet.</summary>
    Hypothesis,

    /// <summary>Early runs, frequently changing parameters.</summary>
    Exploring,

    /// <summary>Parameter sweep / optimisation phase.</summary>
    Optimizing,

    /// <summary>Walk-forward and robustness validation phase.</summary>
    Validating,

    /// <summary>Sealed test set has been run exactly once.</summary>
    FinalTest,

    /// <summary>No longer under active development.</summary>
    Retired
}
