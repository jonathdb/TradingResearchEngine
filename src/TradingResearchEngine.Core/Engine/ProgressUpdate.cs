namespace TradingResearchEngine.Core.Engine;

/// <summary>Progress update for long-running workflows.</summary>
public sealed record ProgressUpdate(
    int CurrentStep,
    int TotalSteps,
    string Message)
{
    /// <summary>Progress as a fraction [0, 1].</summary>
    public double Fraction => TotalSteps > 0 ? (double)CurrentStep / TotalSteps : 0;

    /// <summary>Number of bars processed so far (alias for <see cref="CurrentStep"/>).</summary>
    public int BarsProcessed => CurrentStep;

    /// <summary>Total number of bars in the run (alias for <see cref="TotalSteps"/>). Zero if unknown.</summary>
    public int TotalBars => TotalSteps;
}
