namespace TradingResearchEngine.Application.TickImport;

/// <summary>Status of a tick data import job.</summary>
public enum TickImportStatus
{
    /// <summary>Import is actively downloading tick data.</summary>
    Running,

    /// <summary>All requested trading days have been downloaded.</summary>
    Completed,

    /// <summary>Import failed due to network or processing error.</summary>
    Failed,

    /// <summary>Import was cancelled by the user.</summary>
    Cancelled
}
