namespace TradingResearchEngine.Application.MarketData;

/// <summary>Status of a market data import job.</summary>
public enum MarketDataImportStatus
{
    /// <summary>Download is in progress.</summary>
    Running,

    /// <summary>Download and normalization completed (file may still be invalid).</summary>
    Completed,

    /// <summary>Download or normalization failed.</summary>
    Failed,

    /// <summary>User cancelled the import.</summary>
    Cancelled
}
