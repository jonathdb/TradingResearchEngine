namespace TradingResearchEngine.Application.Configuration;

/// <summary>Options for determining when a strategy's last run is considered stale.</summary>
public sealed class StalenessOptions
{
    /// <summary>Number of days since the last run after which a strategy is considered stale.</summary>
    public int StalenessThresholdDays { get; set; } = 30;
}
