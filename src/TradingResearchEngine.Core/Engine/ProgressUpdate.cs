namespace TradingResearchEngine.Core.Engine;

/// <summary>Progress update for long-running workflows.</summary>
public sealed record ProgressUpdate(
    int CurrentStep,
    int TotalSteps,
    string Message)
{
    /// <summary>Progress as a fraction [0, 1].</summary>
    public double Fraction => TotalSteps > 0 ? (double)CurrentStep / TotalSteps : 0;
}
