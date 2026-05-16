namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Adapter that converts between the legacy <c>Dictionary&lt;string, object&gt;</c>
/// data provider options and the strongly-typed <see cref="DataProviderConfig"/> hierarchy.
/// <para>
/// This adapter maintains backward compatibility at the deserialization boundary:
/// existing JSON config files using flat dictionaries are transparently converted to
/// the discriminated union type at runtime.
/// </para>
/// </summary>
public static class DataProviderConfigAdapter
{
    /// <summary>
    /// Converts a legacy options dictionary to the appropriate <see cref="DataProviderConfig"/> subtype
    /// based on the provider type discriminator.
    /// </summary>
    /// <param name="providerType">The data provider type key (e.g. "csv", "http", "dukascopy").</param>
    /// <param name="dictionary">The legacy string-key dictionary from ScenarioConfig.</param>
    /// <returns>A strongly-typed <see cref="DataProviderConfig"/> instance.</returns>
    public static DataProviderConfig FromDictionary(string providerType, IReadOnlyDictionary<string, object> dictionary)
    {
        return providerType.ToLowerInvariant() switch
        {
            "csv" => ToCsvConfig(dictionary),
            "http" or "rest" => ToHttpConfig(dictionary),
            "dukascopy" => ToDukascopyConfig(dictionary),
            _ => ToCsvConfig(dictionary) // Default fallback for unknown types
        };
    }

    /// <summary>
    /// Converts a <see cref="DataProviderConfig"/> back to a legacy dictionary representation.
    /// Used when constructing ScenarioConfig from typed options for backward compatibility.
    /// </summary>
    /// <param name="config">The typed config to convert.</param>
    /// <returns>A dictionary compatible with the legacy <c>DataProviderOptions</c> field.</returns>
    public static Dictionary<string, object> ToDictionary(DataProviderConfig config)
    {
        var dict = new Dictionary<string, object>();

        // Common fields
        if (config.Symbol is not null)
            dict["Symbol"] = config.Symbol;
        if (config.Interval is not null)
            dict["Interval"] = config.Interval;
        if (config.From.HasValue)
            dict["From"] = config.From.Value.ToString("O");
        if (config.To.HasValue)
            dict["To"] = config.To.Value.ToString("O");

        // Type-specific fields
        switch (config)
        {
            case CsvDataProviderConfig csv:
                if (!string.IsNullOrEmpty(csv.FilePath))
                    dict["FilePath"] = csv.FilePath;
                if (csv.DateFormat != "yyyy-MM-dd")
                    dict["DateFormat"] = csv.DateFormat;
                if (!csv.HasHeader)
                    dict["HasHeader"] = csv.HasHeader;
                break;

            case HttpDataProviderConfig http:
                if (!string.IsNullOrEmpty(http.BaseUrl))
                    dict["BaseUrl"] = http.BaseUrl;
                if (!string.IsNullOrEmpty(http.ApiKey))
                    dict["ApiKey"] = http.ApiKey;
                if (http.TimeoutSeconds != 30)
                    dict["Timeout"] = http.TimeoutSeconds;
                break;

            case DukascopyDataProviderConfig dukascopy:
                if (dukascopy.CacheDirectory != "data/dukascopy-cache")
                    dict["CacheDirectory"] = dukascopy.CacheDirectory;
                if (dukascopy.CacheTtlHours != 24)
                    dict["CacheTtl"] = dukascopy.CacheTtlHours;
                break;
        }

        return dict;
    }

    private static CsvDataProviderConfig ToCsvConfig(IReadOnlyDictionary<string, object> dictionary)
    {
        return new CsvDataProviderConfig
        {
            Symbol = GetStringOrNull(dictionary, "Symbol"),
            Interval = GetStringOrNull(dictionary, "Interval"),
            From = GetDateTimeOffsetOrNull(dictionary, "From"),
            To = GetDateTimeOffsetOrNull(dictionary, "To"),
            FilePath = GetStringOrDefault(dictionary, "FilePath", ""),
            DateFormat = GetStringOrDefault(dictionary, "DateFormat", "yyyy-MM-dd"),
            HasHeader = GetBoolOrDefault(dictionary, "HasHeader", true)
        };
    }

    private static HttpDataProviderConfig ToHttpConfig(IReadOnlyDictionary<string, object> dictionary)
    {
        var timeoutSeconds = 30;
        if (dictionary.TryGetValue("Timeout", out var timeout))
        {
            if (timeout is int i) timeoutSeconds = i;
            else if (timeout is double d) timeoutSeconds = (int)d;
            else if (timeout is TimeSpan ts) timeoutSeconds = (int)ts.TotalSeconds;
            else if (int.TryParse(timeout?.ToString(), out var parsed)) timeoutSeconds = parsed;
        }

        return new HttpDataProviderConfig
        {
            Symbol = GetStringOrNull(dictionary, "Symbol"),
            Interval = GetStringOrNull(dictionary, "Interval"),
            From = GetDateTimeOffsetOrNull(dictionary, "From"),
            To = GetDateTimeOffsetOrNull(dictionary, "To"),
            BaseUrl = GetStringOrDefault(dictionary, "BaseUrl", ""),
            ApiKey = GetStringOrDefault(dictionary, "ApiKey", ""),
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static DukascopyDataProviderConfig ToDukascopyConfig(IReadOnlyDictionary<string, object> dictionary)
    {
        var cacheTtlHours = 24;
        if (dictionary.TryGetValue("CacheTtl", out var ttl))
        {
            if (ttl is int i) cacheTtlHours = i;
            else if (ttl is double d) cacheTtlHours = (int)d;
            else if (ttl is TimeSpan ts) cacheTtlHours = (int)ts.TotalHours;
            else if (int.TryParse(ttl?.ToString(), out var parsed)) cacheTtlHours = parsed;
        }
        else if (dictionary.TryGetValue("CacheTtlHours", out var ttlH))
        {
            if (ttlH is int i) cacheTtlHours = i;
            else if (int.TryParse(ttlH?.ToString(), out var parsed)) cacheTtlHours = parsed;
        }

        return new DukascopyDataProviderConfig
        {
            Symbol = GetStringOrNull(dictionary, "Symbol"),
            Interval = GetStringOrNull(dictionary, "Interval"),
            From = GetDateTimeOffsetOrNull(dictionary, "From"),
            To = GetDateTimeOffsetOrNull(dictionary, "To"),
            CacheDirectory = GetStringOrDefault(dictionary, "CacheDirectory", "data/dukascopy-cache"),
            CacheTtlHours = cacheTtlHours
        };
    }

    private static string? GetStringOrNull(IReadOnlyDictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val is not null)
            return val.ToString();
        return null;
    }

    private static string GetStringOrDefault(IReadOnlyDictionary<string, object> dict, string key, string defaultValue)
    {
        if (dict.TryGetValue(key, out var val) && val is not null)
            return val.ToString() ?? defaultValue;
        return defaultValue;
    }

    private static bool GetBoolOrDefault(IReadOnlyDictionary<string, object> dict, string key, bool defaultValue)
    {
        if (!dict.TryGetValue(key, out var val)) return defaultValue;
        if (val is bool b) return b;
        if (bool.TryParse(val?.ToString(), out var parsed)) return parsed;
        return defaultValue;
    }

    private static DateTimeOffset? GetDateTimeOffsetOrNull(IReadOnlyDictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || val is null) return null;
        if (val is DateTimeOffset dto) return dto;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val.ToString() is string s && DateTimeOffset.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
