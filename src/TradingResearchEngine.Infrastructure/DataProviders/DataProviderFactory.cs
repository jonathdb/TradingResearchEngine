using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>Creates data providers based on type name and options from ScenarioConfig.</summary>
public sealed class DataProviderFactory : IDataProviderFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly CsvDataProviderOptions _csvDefaults;
    private readonly HttpDataProviderOptions _httpDefaults;
    private readonly DukascopyDataProviderOptions _dukascopyDefaults;

    /// <inheritdoc cref="DataProviderFactory"/>
    public DataProviderFactory(
        ILoggerFactory loggerFactory,
        IOptions<CsvDataProviderOptions> csvOptions,
        IOptions<HttpDataProviderOptions> httpOptions,
        IOptions<DukascopyDataProviderOptions> dukascopyOptions,
        IHttpClientFactory? httpClientFactory = null)
    {
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _csvDefaults = csvOptions.Value;
        _httpDefaults = httpOptions.Value;
        _dukascopyDefaults = dukascopyOptions.Value;
    }

    /// <summary>
    /// Initializes a new <see cref="DataProviderFactory"/> without typed options.
    /// Used for backward compatibility and testing scenarios.
    /// </summary>
    public DataProviderFactory(ILoggerFactory loggerFactory, IHttpClientFactory? httpClientFactory = null)
    {
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _csvDefaults = new CsvDataProviderOptions();
        _httpDefaults = new HttpDataProviderOptions();
        _dukascopyDefaults = new DukascopyDataProviderOptions();
    }

    /// <inheritdoc/>
    public IDataProvider Create(string providerType, Dictionary<string, object> options)
    {
        return providerType.ToLowerInvariant() switch
        {
            "csv" => CreateCsvProvider(options),
            "http" or "rest" => CreateHttpProvider(options),
            "memory" or "inmemory" => CreateInMemoryProvider(options),
            "dukascopy" => CreateDukascopyProvider(options),
            _ => throw new InvalidOperationException($"Unknown data provider type: '{providerType}'. " +
                $"Supported: csv, http, memory, dukascopy")
        };
    }

    /// <inheritdoc/>
    public IDataProvider Create(DataProviderConfig config)
    {
        return config switch
        {
            CsvDataProviderConfig csv => CreateCsvProviderFromTyped(csv),
            HttpDataProviderConfig http => CreateHttpProviderFromTyped(http),
            DukascopyDataProviderConfig dukascopy => CreateDukascopyProviderFromTyped(dukascopy),
            _ => Create(config.ProviderType, DataProviderConfigAdapter.ToDictionary(config))
        };
    }

    private CsvDataProvider CreateCsvProvider(Dictionary<string, object> options)
    {
        // Primary path: use typed options via compatibility adapter
        var typed = DataProviderOptionsAdapter.ToCsvOptions(options);

        // Fall back to IOptions<T> defaults when dictionary doesn't specify a value
        var filePath = !string.IsNullOrEmpty(typed.FilePath)
            ? typed.FilePath
            : (!string.IsNullOrEmpty(_csvDefaults.FilePath) ? _csvDefaults.FilePath : "data.csv");

        // Resolve relative paths: try working directory first, then walk up to find the file
        if (!Path.IsPathRooted(filePath) && !File.Exists(filePath))
        {
            var candidate = FindFileUpwards(filePath);
            if (candidate is not null) filePath = candidate;
        }
        return new CsvDataProvider(filePath, _loggerFactory.CreateLogger<CsvDataProvider>());
    }

    private HttpRestDataProvider CreateHttpProvider(Dictionary<string, object> options)
    {
        // Primary path: use typed options via compatibility adapter
        var typed = DataProviderOptionsAdapter.ToHttpOptions(options);

        // Fall back to IOptions<T> defaults when dictionary doesn't specify a value
        var baseUrl = !string.IsNullOrEmpty(typed.BaseUrl)
            ? typed.BaseUrl
            : _httpDefaults.BaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("HttpRestDataProvider requires a 'BaseUrl' in DataProviderOptions.");

        var client = _httpClientFactory?.CreateClient("DataProvider") ?? new HttpClient();

        // Apply timeout from typed options (dictionary override → IOptions<T> default)
        var timeout = typed.Timeout != TimeSpan.FromSeconds(30) ? typed.Timeout : _httpDefaults.Timeout;
        if (timeout > TimeSpan.Zero)
            client.Timeout = timeout;

        return new HttpRestDataProvider(client, baseUrl);
    }

    private static InMemoryDataProvider CreateInMemoryProvider(Dictionary<string, object> options)
    {
        if (options.TryGetValue("FilteredBars", out var barsObj) && barsObj is IReadOnlyList<BarRecord> bars)
            return new InMemoryDataProvider(bars);
        return new InMemoryDataProvider(Array.Empty<BarRecord>());
    }

    private DukascopyDataProvider CreateDukascopyProvider(Dictionary<string, object> options)
    {
        // Primary path: use typed options via compatibility adapter
        var typed = DataProviderOptionsAdapter.ToDukascopyOptions(options);

        // Fall back to IOptions<T> defaults when dictionary doesn't specify a value
        var cacheDir = typed.CacheDirectory != "data/dukascopy-cache"
            ? typed.CacheDirectory
            : _dukascopyDefaults.CacheDirectory;

        var client = _httpClientFactory?.CreateClient("Dukascopy") ?? new HttpClient();
        return new DukascopyDataProvider(client, _loggerFactory.CreateLogger<DukascopyDataProvider>(), cacheDir: cacheDir);
    }

    private CsvDataProvider CreateCsvProviderFromTyped(CsvDataProviderConfig config)
    {
        var filePath = !string.IsNullOrEmpty(config.FilePath)
            ? config.FilePath
            : (!string.IsNullOrEmpty(_csvDefaults.FilePath) ? _csvDefaults.FilePath : "data.csv");

        if (!Path.IsPathRooted(filePath) && !File.Exists(filePath))
        {
            var candidate = FindFileUpwards(filePath);
            if (candidate is not null) filePath = candidate;
        }
        return new CsvDataProvider(filePath, _loggerFactory.CreateLogger<CsvDataProvider>());
    }

    private HttpRestDataProvider CreateHttpProviderFromTyped(HttpDataProviderConfig config)
    {
        var baseUrl = !string.IsNullOrEmpty(config.BaseUrl)
            ? config.BaseUrl
            : _httpDefaults.BaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("HttpRestDataProvider requires a 'BaseUrl' in DataProviderConfig.");

        var client = _httpClientFactory?.CreateClient("DataProvider") ?? new HttpClient();

        var timeout = config.TimeoutSeconds != 30
            ? TimeSpan.FromSeconds(config.TimeoutSeconds)
            : _httpDefaults.Timeout;
        if (timeout > TimeSpan.Zero)
            client.Timeout = timeout;

        return new HttpRestDataProvider(client, baseUrl);
    }

    private DukascopyDataProvider CreateDukascopyProviderFromTyped(DukascopyDataProviderConfig config)
    {
        var cacheDir = config.CacheDirectory != "data/dukascopy-cache"
            ? config.CacheDirectory
            : _dukascopyDefaults.CacheDirectory;

        var client = _httpClientFactory?.CreateClient("Dukascopy") ?? new HttpClient();
        return new DukascopyDataProvider(client, _loggerFactory.CreateLogger<DukascopyDataProvider>(), cacheDir: cacheDir);
    }

    /// <summary>
    /// Walks up from the current directory looking for a relative file path.
    /// Handles the case where the Web project runs from src/TradingResearchEngine.Web/
    /// but the file is relative to the solution root.
    /// </summary>
    private static string? FindFileUpwards(string relativePath)
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6; i++) // walk up max 6 levels
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
