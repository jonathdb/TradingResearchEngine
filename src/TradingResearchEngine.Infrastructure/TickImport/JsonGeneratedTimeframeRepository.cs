using System.Text.Json;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.Infrastructure.TickImport;

/// <summary>
/// JSON file-based implementation of <see cref="IGeneratedTimeframeRepository"/>.
/// Stores each record as a separate JSON file in the configured base directory.
/// </summary>
public sealed class JsonGeneratedTimeframeRepository : IGeneratedTimeframeRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseDir;

    /// <summary>Initializes a new instance of <see cref="JsonGeneratedTimeframeRepository"/>.</summary>
    /// <param name="baseDir">Base directory for storing generated timeframe JSON files.</param>
    public JsonGeneratedTimeframeRepository(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public Task<GeneratedTimeframeRecord?> GetAsync(string recordId, CancellationToken ct = default)
    {
        var path = GetPath(recordId);
        if (!File.Exists(path)) return Task.FromResult<GeneratedTimeframeRecord?>(null);
        var json = File.ReadAllText(path);
        return Task.FromResult(JsonSerializer.Deserialize<GeneratedTimeframeRecord>(json, JsonOpts));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<GeneratedTimeframeRecord>> ListByImportAsync(
        string tickImportId, CancellationToken ct = default)
    {
        var results = new List<GeneratedTimeframeRecord>();
        if (!Directory.Exists(_baseDir))
            return Task.FromResult<IReadOnlyList<GeneratedTimeframeRecord>>(results);

        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<GeneratedTimeframeRecord>(json, JsonOpts);
                if (record is not null && record.TickImportId == tickImportId)
                    results.Add(record);
            }
            catch { /* skip malformed files */ }
        }

        return Task.FromResult<IReadOnlyList<GeneratedTimeframeRecord>>(results);
    }

    /// <inheritdoc/>
    public Task SaveAsync(GeneratedTimeframeRecord record, CancellationToken ct = default)
    {
        var path = GetPath(record.RecordId);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(path, json);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string recordId, CancellationToken ct = default)
    {
        var path = GetPath(recordId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string recordId) => Path.Combine(_baseDir, $"{recordId}.json");
}
