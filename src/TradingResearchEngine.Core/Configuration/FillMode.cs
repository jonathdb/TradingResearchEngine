namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Controls when risk-approved orders are filled relative to the bar that generated them.
/// </summary>
public enum FillMode
{
    /// <summary>
    /// Orders fill at the next bar's Open price. This is the correct default
    /// that eliminates look-ahead bias — the strategy cannot observe and fill
    /// at the same bar's Close price.
    /// </summary>
    NextBarOpen,

    /// <summary>
    /// V1 legacy mode: orders fill at the same bar's Close price.
    /// Introduces look-ahead bias. Use only for backward-compatible test fixtures.
    /// </summary>
    SameBarClose
}
