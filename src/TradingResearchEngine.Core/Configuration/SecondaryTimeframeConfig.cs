namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Configuration for a secondary timeframe data source used in multi-timeframe strategies.
/// Specifies the timeframe label, data provider type, and provider-specific options.
/// </summary>
public sealed record SecondaryTimeframeConfig(
    /// <summary>The timeframe label (e.g. "H4", "Daily", "W1").</summary>
    string Timeframe,
    /// <summary>The data provider implementation key (e.g. "csv", "http", "dukascopy").</summary>
    string DataProviderType,
    /// <summary>Provider-specific options (e.g. file path, URL, symbol).</summary>
    Dictionary<string, object> DataProviderOptions);
