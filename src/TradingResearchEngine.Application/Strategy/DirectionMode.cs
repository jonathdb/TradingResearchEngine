namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Controls which signal directions a strategy emits.
/// V6: Enables bidirectional strategy support.
/// </summary>
public enum DirectionMode
{
    /// <summary>Strategy emits Long entries only.</summary>
    Long,

    /// <summary>Strategy emits Short entries only.</summary>
    Short,

    /// <summary>Strategy emits both Long and Short entries.</summary>
    Both
}
