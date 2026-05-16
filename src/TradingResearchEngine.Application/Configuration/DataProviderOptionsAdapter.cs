namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Compatibility adapter that maps between the legacy <c>Dictionary&lt;string, object&gt;</c>
/// data provider options and the strongly-typed option classes.
/// <para>
/// This adapter maintains backward compatibility at the JSON configuration ingestion boundary:
/// existing config files using <c>DataProviderOptions</c> dictionaries continue to work,
/// while new code can access typed properties directly.
/// </para>
/// </summary>
public static class DataProviderOptionsAdapter
{
    /// <summary>
    /// Extracts a <see cref="CsvDataProviderOptions"/> from a legacy options dictionary.
    /// Missing keys fall back to the typed class defaults.
    /// </summary>
    /// <param name="dictionary">The legacy string-key dictionary from ScenarioConfig.</param>
    /// <returns>A populated <see cref="CsvDataProviderOptions"/> instance.</returns>
    public static CsvDataProviderOptions ToCsvOptions(IReadOnlyDictionary<string, object> dictionary)
    {
        var options = new CsvDataProviderOptions();

        if (dictionary.TryGetValue("FilePath", out var fp) && fp is not null)
            options.FilePath = fp.ToString() ?? "";

        if (dictionary.TryGetValue("DateFormat", out var df) && df is not null)
            options.DateFormat = df.ToString() ?? options.DateFormat;

        if (dictionary.TryGetValue("HasHeader", out var hh))
        {
            if (hh is bool b)
                options.HasHeader = b;
            else if (bool.TryParse(hh?.ToString(), out var parsed))
                options.HasHeader = parsed;
        }

        return options;
    }

    /// <summary>
    /// Extracts an <see cref="HttpDataProviderOptions"/> from a legacy options dictionary.
    /// Missing keys fall back to the typed class defaults.
    /// </summary>
    /// <param name="dictionary">The legacy string-key dictionary from ScenarioConfig.</param>
    /// <returns>A populated <see cref="HttpDataProviderOptions"/> instance.</returns>
    public static HttpDataProviderOptions ToHttpOptions(IReadOnlyDictionary<string, object> dictionary)
    {
        var options = new HttpDataProviderOptions();

        if (dictionary.TryGetValue("BaseUrl", out var url) && url is not null)
            options.BaseUrl = url.ToString() ?? "";

        if (dictionary.TryGetValue("ApiKey", out var key) && key is not null)
            options.ApiKey = key.ToString() ?? "";

        if (dictionary.TryGetValue("Timeout", out var timeout))
        {
            if (timeout is TimeSpan ts)
                options.Timeout = ts;
            else if (timeout is int seconds)
                options.Timeout = TimeSpan.FromSeconds(seconds);
            else if (timeout is double dblSeconds)
                options.Timeout = TimeSpan.FromSeconds(dblSeconds);
            else if (int.TryParse(timeout?.ToString(), out var parsedSeconds))
                options.Timeout = TimeSpan.FromSeconds(parsedSeconds);
        }

        return options;
    }

    /// <summary>
    /// Extracts a <see cref="DukascopyDataProviderOptions"/> from a legacy options dictionary.
    /// Missing keys fall back to the typed class defaults.
    /// </summary>
    /// <param name="dictionary">The legacy string-key dictionary from ScenarioConfig.</param>
    /// <returns>A populated <see cref="DukascopyDataProviderOptions"/> instance.</returns>
    public static DukascopyDataProviderOptions ToDukascopyOptions(IReadOnlyDictionary<string, object> dictionary)
    {
        var options = new DukascopyDataProviderOptions();

        if (dictionary.TryGetValue("CacheDirectory", out var dir) && dir is not null)
            options.CacheDirectory = dir.ToString() ?? options.CacheDirectory;

        if (dictionary.TryGetValue("CacheTtl", out var ttl))
        {
            if (ttl is TimeSpan ts)
                options.CacheTtl = ts;
            else if (ttl is int hours)
                options.CacheTtl = TimeSpan.FromHours(hours);
            else if (ttl is double dblHours)
                options.CacheTtl = TimeSpan.FromHours(dblHours);
            else if (int.TryParse(ttl?.ToString(), out var parsedHours))
                options.CacheTtl = TimeSpan.FromHours(parsedHours);
        }

        return options;
    }

    /// <summary>
    /// Converts a <see cref="CsvDataProviderOptions"/> back to a legacy dictionary representation.
    /// Used when constructing ScenarioConfig from typed options for backward compatibility.
    /// </summary>
    /// <param name="options">The typed options to convert.</param>
    /// <returns>A dictionary compatible with the legacy <c>DataProviderOptions</c> field.</returns>
    public static Dictionary<string, object> ToDictionary(CsvDataProviderOptions options)
    {
        var dict = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(options.FilePath))
            dict["FilePath"] = options.FilePath;
        if (options.DateFormat != "yyyy-MM-dd")
            dict["DateFormat"] = options.DateFormat;
        if (!options.HasHeader)
            dict["HasHeader"] = options.HasHeader;
        return dict;
    }

    /// <summary>
    /// Converts an <see cref="HttpDataProviderOptions"/> back to a legacy dictionary representation.
    /// </summary>
    /// <param name="options">The typed options to convert.</param>
    /// <returns>A dictionary compatible with the legacy <c>DataProviderOptions</c> field.</returns>
    public static Dictionary<string, object> ToDictionary(HttpDataProviderOptions options)
    {
        var dict = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(options.BaseUrl))
            dict["BaseUrl"] = options.BaseUrl;
        if (!string.IsNullOrEmpty(options.ApiKey))
            dict["ApiKey"] = options.ApiKey;
        if (options.Timeout != TimeSpan.FromSeconds(30))
            dict["Timeout"] = (int)options.Timeout.TotalSeconds;
        return dict;
    }

    /// <summary>
    /// Converts a <see cref="DukascopyDataProviderOptions"/> back to a legacy dictionary representation.
    /// </summary>
    /// <param name="options">The typed options to convert.</param>
    /// <returns>A dictionary compatible with the legacy <c>DataProviderOptions</c> field.</returns>
    public static Dictionary<string, object> ToDictionary(DukascopyDataProviderOptions options)
    {
        var dict = new Dictionary<string, object>();
        if (options.CacheDirectory != "data/dukascopy-cache")
            dict["CacheDirectory"] = options.CacheDirectory;
        if (options.CacheTtl != TimeSpan.FromHours(24))
            dict["CacheTtl"] = (int)options.CacheTtl.TotalHours;
        return dict;
    }
}
