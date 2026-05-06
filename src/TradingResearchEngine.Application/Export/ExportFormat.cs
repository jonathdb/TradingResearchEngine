namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Target platform format for strategy code export.
/// </summary>
public enum ExportFormat
{
    /// <summary>MetaTrader 4 Expert Advisor (.mq4).</summary>
    MQL4,

    /// <summary>MetaTrader 5 Expert Advisor (.mq5).</summary>
    MQL5,

    /// <summary>TradingView Pine Script v6 (.pine).</summary>
    PineScript
}
