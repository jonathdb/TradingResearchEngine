using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>Creates data providers based on type name and options from ScenarioConfig.</summary>
public sealed class DataProviderFactory : IDataProviderFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <inheritdoc cref="DataProviderFactory"/>
    public DataProviderFactory(ILoggerFactory loggerFactory, IHttpClientFactory? httpClientFactory = null)
    {
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
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

    private CsvDataProvider CreateCsvProvider(Dictionary<string, object> options)
    {
        var filePath = options.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "data.csv" : "data.csv";

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
        var baseUrl = options.TryGetValue("BaseUrl", out var url) ? url?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("HttpRestDataProvider requires a 'BaseUrl' in DataProviderOptions.");
        var client = _httpClientFactory?.CreateClient("DataProvider") ?? new HttpClient();
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
        var client = _httpClientFactory?.CreateClient("Dukascopy") ?? new HttpClient();
        return new DukascopyDataProvider(client, _loggerFactory.CreateLogger<DukascopyDataProvider>());
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
