using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Extension methods providing strongly-typed property access to <see cref="DataConfig"/>
/// and <see cref="Core.Configuration.ScenarioConfig"/> data provider options.
/// <para>
/// These extensions serve as the primary typed access path, replacing scattered string-key
/// dictionary lookups in DataHandler, workflows, and providers. When a <see cref="DataProviderConfig"/>
/// typed config is available on the <see cref="DataConfig"/>, it is preferred over the legacy dictionary.
/// The underlying dictionary is preserved for backward compatibility with existing JSON configuration files.
/// </para>
/// </summary>
public static class DataConfigExtensions
{
    /// <summary>
    /// Gets the typed CSV data provider options from the data config.
    /// Prefers <see cref="DataConfig.TypedProviderConfig"/> when available,
    /// falling back to the legacy dictionary adapter.
    /// </summary>
    /// <param name="dataConfig">The data configuration containing provider options.</param>
    /// <returns>A populated <see cref="CsvDataProviderOptions"/> instance.</returns>
    public static CsvDataProviderOptions GetCsvOptions(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is CsvDataProviderConfig typed)
        {
            return new CsvDataProviderOptions
            {
                FilePath = typed.FilePath,
                DateFormat = typed.DateFormat,
                HasHeader = typed.HasHeader
            };
        }

#pragma warning disable CS0618 // Obsolete member access for backward compatibility
        return DataProviderOptionsAdapter.ToCsvOptions(dataConfig.DataProviderOptions);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the typed HTTP data provider options from the data config.
    /// Prefers <see cref="DataConfig.TypedProviderConfig"/> when available,
    /// falling back to the legacy dictionary adapter.
    /// </summary>
    /// <param name="dataConfig">The data configuration containing provider options.</param>
    /// <returns>A populated <see cref="HttpDataProviderOptions"/> instance.</returns>
    public static HttpDataProviderOptions GetHttpOptions(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is HttpDataProviderConfig typed)
        {
            return new HttpDataProviderOptions
            {
                BaseUrl = typed.BaseUrl,
                ApiKey = typed.ApiKey,
                Timeout = TimeSpan.FromSeconds(typed.TimeoutSeconds)
            };
        }

#pragma warning disable CS0618 // Obsolete member access for backward compatibility
        return DataProviderOptionsAdapter.ToHttpOptions(dataConfig.DataProviderOptions);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the typed Dukascopy data provider options from the data config.
    /// Prefers <see cref="DataConfig.TypedProviderConfig"/> when available,
    /// falling back to the legacy dictionary adapter.
    /// </summary>
    /// <param name="dataConfig">The data configuration containing provider options.</param>
    /// <returns>A populated <see cref="DukascopyDataProviderOptions"/> instance.</returns>
    public static DukascopyDataProviderOptions GetDukascopyOptions(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is DukascopyDataProviderConfig typed)
        {
            return new DukascopyDataProviderOptions
            {
                CacheDirectory = typed.CacheDirectory,
                CacheTtl = TimeSpan.FromHours(typed.CacheTtlHours)
            };
        }

#pragma warning disable CS0618 // Obsolete member access for backward compatibility
        return DataProviderOptionsAdapter.ToDukascopyOptions(dataConfig.DataProviderOptions);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the symbol from the data config's typed provider configuration or legacy dictionary.
    /// This is a common field used across all provider types for data identification.
    /// </summary>
    /// <param name="dataConfig">The data configuration.</param>
    /// <returns>The symbol string, or empty string if not specified.</returns>
    public static string GetSymbol(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is { Symbol: not null } typed)
            return typed.Symbol;

#pragma warning disable CS0618
        return dataConfig.DataProviderOptions.GetSymbol();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the interval from the data config's typed provider configuration or legacy dictionary.
    /// Defaults to "1D" (daily) when not specified.
    /// </summary>
    /// <param name="dataConfig">The data configuration.</param>
    /// <returns>The interval string, defaulting to "1D".</returns>
    public static string GetInterval(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is { Interval: not null } typed)
            return typed.Interval;

#pragma warning disable CS0618
        return dataConfig.DataProviderOptions.GetInterval();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the "From" date from the data config's typed provider configuration or legacy dictionary.
    /// Returns <see cref="DateTimeOffset.MinValue"/> when not specified.
    /// </summary>
    /// <param name="dataConfig">The data configuration.</param>
    /// <returns>The parsed start date, or <see cref="DateTimeOffset.MinValue"/>.</returns>
    public static DateTimeOffset GetFrom(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is { From: not null } typed)
            return typed.From.Value;

#pragma warning disable CS0618
        return dataConfig.DataProviderOptions.GetFrom();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the "To" date from the data config's typed provider configuration or legacy dictionary.
    /// Returns <see cref="DateTimeOffset.MaxValue"/> when not specified.
    /// </summary>
    /// <param name="dataConfig">The data configuration.</param>
    /// <returns>The parsed end date, or <see cref="DateTimeOffset.MaxValue"/>.</returns>
    public static DateTimeOffset GetTo(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is { To: not null } typed)
            return typed.To.Value;

#pragma warning disable CS0618
        return dataConfig.DataProviderOptions.GetTo();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the file path from the data config's typed provider configuration or legacy dictionary.
    /// Returns empty string when not specified.
    /// </summary>
    /// <param name="dataConfig">The data configuration.</param>
    /// <returns>The file path string, or empty string if not specified.</returns>
    public static string GetFilePath(this DataConfig dataConfig)
    {
        if (dataConfig.TypedProviderConfig is CsvDataProviderConfig csv)
            return csv.FilePath;

#pragma warning disable CS0618
        return dataConfig.DataProviderOptions.GetFilePath();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Gets the symbol from the data provider options dictionary.
    /// This is a common field used across all provider types for data identification.
    /// </summary>
    /// <param name="options">The provider options dictionary.</param>
    /// <returns>The symbol string, or empty string if not specified.</returns>
    public static string GetSymbol(this IReadOnlyDictionary<string, object> options) =>
        options.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";

    /// <summary>
    /// Gets the interval from the data provider options dictionary.
    /// Defaults to "1D" (daily) when not specified.
    /// </summary>
    /// <param name="options">The provider options dictionary.</param>
    /// <returns>The interval string, defaulting to "1D".</returns>
    public static string GetInterval(this IReadOnlyDictionary<string, object> options) =>
        options.TryGetValue("Interval", out var i) ? i?.ToString() ?? "1D" : "1D";

    /// <summary>
    /// Gets the "From" date from the data provider options dictionary.
    /// Returns <see cref="DateTimeOffset.MinValue"/> when not specified.
    /// </summary>
    /// <param name="options">The provider options dictionary.</param>
    /// <returns>The parsed start date, or <see cref="DateTimeOffset.MinValue"/>.</returns>
    public static DateTimeOffset GetFrom(this IReadOnlyDictionary<string, object> options) =>
        ParseDateTimeOffset(options, "From", DateTimeOffset.MinValue);

    /// <summary>
    /// Gets the "To" date from the data provider options dictionary.
    /// Returns <see cref="DateTimeOffset.MaxValue"/> when not specified.
    /// </summary>
    /// <param name="options">The provider options dictionary.</param>
    /// <returns>The parsed end date, or <see cref="DateTimeOffset.MaxValue"/>.</returns>
    public static DateTimeOffset GetTo(this IReadOnlyDictionary<string, object> options) =>
        ParseDateTimeOffset(options, "To", DateTimeOffset.MaxValue);

    /// <summary>
    /// Gets the file path from the data provider options dictionary.
    /// Returns empty string when not specified.
    /// </summary>
    /// <param name="options">The provider options dictionary.</param>
    /// <returns>The file path string, or empty string if not specified.</returns>
    public static string GetFilePath(this IReadOnlyDictionary<string, object> options) =>
        options.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "";

    private static DateTimeOffset ParseDateTimeOffset(
        IReadOnlyDictionary<string, object> opts, string key, DateTimeOffset fallback)
    {
        if (!opts.TryGetValue(key, out var val)) return fallback;
        if (val is DateTimeOffset dto) return dto;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val is string str && DateTimeOffset.TryParse(str, out var parsed)) return parsed;
        // Handle System.Text.Json's JsonElement
        if (val?.ToString() is string s && DateTimeOffset.TryParse(s, out var parsed2)) return parsed2;
        return fallback;
    }
}
