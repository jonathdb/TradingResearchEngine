namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Configuration options for the tick import downloader.
/// Bound via IOptions&lt;TickImportOptions&gt; from appsettings.json.
/// </summary>
public sealed class TickImportOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "TickImport";

    /// <summary>Maximum number of concurrent HTTP downloads. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Maximum connections per server for HttpClient. Matches MaxConcurrency by default.</summary>
    public int MaxConnectionsPerServer { get; set; } = 10;

    /// <summary>Maximum retry attempts for transient HTTP failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base directory for the tick cache. Defaults to data/tick-cache.</summary>
    public string CacheDirectory { get; set; } = "data/tick-cache";
}
