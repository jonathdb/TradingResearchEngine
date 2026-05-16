namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Strongly-typed configuration options for the CSV data provider.
/// Bound from <c>appsettings.json:DataProviders:Csv</c> via <c>IOptions&lt;CsvDataProviderOptions&gt;</c>.
/// </summary>
public sealed class CsvDataProviderOptions
{
    /// <summary>Path to the CSV data file. Relative paths are resolved from the working directory.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Date format string used when parsing timestamp columns. Defaults to ISO 8601.</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>Whether the CSV file contains a header row. Defaults to <c>true</c>.</summary>
    public bool HasHeader { get; set; } = true;
}

/// <summary>
/// Strongly-typed configuration options for the HTTP REST data provider.
/// Bound from <c>appsettings.json:DataProviders:Http</c> via <c>IOptions&lt;HttpDataProviderOptions&gt;</c>.
/// </summary>
public sealed class HttpDataProviderOptions
{
    /// <summary>Base URL for the HTTP data provider endpoint.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API key for authenticated data provider endpoints. Empty when not required.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>HTTP request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Strongly-typed configuration options for the Dukascopy data provider.
/// Bound from <c>appsettings.json:DataProviders:Dukascopy</c> via <c>IOptions&lt;DukascopyDataProviderOptions&gt;</c>.
/// </summary>
public sealed class DukascopyDataProviderOptions
{
    /// <summary>Directory for caching downloaded Dukascopy data files.</summary>
    public string CacheDirectory { get; set; } = "data/dukascopy-cache";

    /// <summary>Time-to-live for cached data before re-download is triggered.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);
}
