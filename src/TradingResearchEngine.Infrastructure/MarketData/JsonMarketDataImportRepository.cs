using System.Text.Json;
using System.Text.Json.Serialization;
using TradingResearchEngine.Application.MarketData;

namespace TradingResearchEngine.Infrastructure.MarketData;

/// <summary>
/// JSON file-based implementation of <see cref="IMarketDataImportRepository"/>.
/// Stores each record as imports/{importId}.json.
/// </summary>
public sealed class JsonMarketDataImportRepository : IMarketDataImportRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _baseDir;

    /// <inheritdoc cref="JsonMarketDataImportRepository"/>
    public JsonMarketDataImportRepository(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public Task<MarketDataImportRecord?> GetAsync(string importId, CancellationToken ct = default)
    {
        var path = GetPath(importId);
        if (!File.Exists(path)) return Task.FromResult<MarketDataImportRecord?>(null);
        var json = File.ReadAllText(path);
        return Task.FromResult(JsonSerializer.Deserialize<MarketDataImportRecord>(json, JsonOpts));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MarketDataImportRecord>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<MarketDataImportRecord>();
        if (!Directory.Exists(_baseDir))
            return Task.FromResult<IReadOnlyList<MarketDataImportRecord>>(results);

        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<MarketDataImportRecord>(json, JsonOpts);
                if (record is not null) results.Add(record);
            }
            catch { /* skip malformed files */ }
        }
        return Task.FromResult<IReadOnlyList<MarketDataImportRecord>>(results);
    }

    /// <inheritdoc/>
    public Task SaveAsync(MarketDataImportRecord record, CancellationToken ct = default)
    {
        var path = GetPath(record.ImportId);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(path, json);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string importId, CancellationToken ct = default)
    {
        var path = GetPath(importId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string importId) => Path.Combine(_baseDir, $"{importId}.json");
}
