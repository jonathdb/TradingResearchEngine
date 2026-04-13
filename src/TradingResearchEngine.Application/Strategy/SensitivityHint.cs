namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Indicates how sensitive a strategy parameter is to overfitting risk.
/// Surfaced in the builder UI as a visual badge.
/// </summary>
public enum SensitivityHint
{
    /// <summary>Low overfitting risk — parameter has broad stable region.</summary>
    Low,
    /// <summary>Medium overfitting risk — parameter affects results but has reasonable stability.</summary>
    Medium,
    /// <summary>High overfitting risk — small changes materially affect results.</summary>
    High
}
