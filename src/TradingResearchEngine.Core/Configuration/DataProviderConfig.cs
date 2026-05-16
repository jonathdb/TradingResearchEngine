using System.Text.Json.Serialization;

namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Sealed discriminated union representing strongly-typed data provider configuration.
/// Replaces the legacy <c>Dictionary&lt;string, object&gt;</c> approach with compile-time
/// type safety while maintaining JSON backward compatibility via a custom converter.
/// </summary>
[JsonConverter(typeof(DataProviderConfigConverter))]
[JsonDerivedType(typeof(CsvDataProviderConfig), typeDiscriminator: "csv")]
[JsonDerivedType(typeof(HttpDataProviderConfig), typeDiscriminator: "http")]
[JsonDerivedType(typeof(DukascopyDataProviderConfig), typeDiscriminator: "dukascopy")]
public abstract record DataProviderConfig
{
    /// <summary>The provider type discriminator used for serialization and factory dispatch.</summary>
    [JsonPropertyName("$type")]
    public abstract string ProviderType { get; }

    /// <summary>Optional symbol identifier for the data source.</summary>
    public string? Symbol { get; init; }

    /// <summary>Optional interval/timeframe for the data (e.g. "1D", "H4").</summary>
    public string? Interval { get; init; }

    /// <summary>Optional start date for data retrieval.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Optional end date for data retrieval.</summary>
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// Strongly-typed configuration for the CSV data provider.
/// Makes file path and format settings compile-time verifiable.
/// </summary>
public sealed record CsvDataProviderConfig : DataProviderConfig
{
    /// <inheritdoc/>
    public override string ProviderType => "csv";

    /// <summary>Path to the CSV data file. Relative paths are resolved from the working directory.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Date format string used when parsing timestamp columns. Defaults to ISO 8601.</summary>
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Whether the CSV file contains a header row. Defaults to <c>true</c>.</summary>
    public bool HasHeader { get; init; } = true;
}

/// <summary>
/// Strongly-typed configuration for the HTTP REST data provider.
/// Makes endpoint and authentication settings compile-time verifiable.
/// </summary>
public sealed record HttpDataProviderConfig : DataProviderConfig
{
    /// <inheritdoc/>
    public override string ProviderType => "http";

    /// <summary>Base URL for the HTTP data provider endpoint.</summary>
    public string BaseUrl { get; init; } = "";

    /// <summary>API key for authenticated data provider endpoints. Empty when not required.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>HTTP request timeout in seconds. Defaults to 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Strongly-typed configuration for the Dukascopy data provider.
/// Makes cache settings compile-time verifiable.
/// </summary>
public sealed record DukascopyDataProviderConfig : DataProviderConfig
{
    /// <inheritdoc/>
    public override string ProviderType => "dukascopy";

    /// <summary>Directory for caching downloaded Dukascopy data files.</summary>
    public string CacheDirectory { get; init; } = "data/dukascopy-cache";

    /// <summary>Time-to-live for cached data in hours before re-download is triggered.</summary>
    public int CacheTtlHours { get; init; } = 24;
}
