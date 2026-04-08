namespace TradingResearchEngine.Application.Risk;

/// <summary>
/// Optional, composable portfolio constraints. Each constraint is independent —
/// enabling one does not require enabling others. Null values mean the constraint is inactive.
/// </summary>
public sealed class PortfolioConstraints
{
    /// <summary>Maximum gross exposure as a percentage of equity (e.g. 100 = 100%).</summary>
    public decimal? MaxGrossExposurePercent { get; set; }

    /// <summary>Maximum capital allocated to a single symbol as a percentage of equity.</summary>
    public decimal? MaxCapitalPerSymbolPercent { get; set; }

    /// <summary>Maximum number of concurrent open positions.</summary>
    public int? MaxConcurrentPositions { get; set; }

    /// <summary>Minimum bars to wait after closing a position before re-entering the same symbol.</summary>
    public int? CooldownBars { get; set; }

    /// <summary>Maximum daily loss as a percentage of starting equity. Halts trading for the day when breached.</summary>
    public decimal? MaxDailyLossPercent { get; set; }

    /// <summary>Maximum trailing drawdown as a percentage of peak equity. Halts trading when breached.</summary>
    public decimal? MaxTrailingDrawdownPercent { get; set; }
}
