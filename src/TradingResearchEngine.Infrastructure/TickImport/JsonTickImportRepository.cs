using System.Text.Json;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.Infrastructure.TickImport;

/// <summary>
/// JSON file-based implementation of <see cref="ITickImportRepository"/>.
/// Stores each record as a separate JSON file in the configured base directory.
/// </summary>
public sealed class JsonTickImportRepository : ITickImportRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseDir;

    /// <summary>Initializes a new instance of <see cref="JsonTickImportRepository"/>.</summary>
    /// <param name="baseDir">Base directory for storing tick import JSON files.</param>
    public JsonTickImportRepository(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public Task<TickImportRecord?> GetAsync(string importId, CancellationToken ct = default)
    {
        var path = GetPath(importId);
        if (!File.Exists(path)) return Task.FromResult<TickImportRecord?>(null);
        var json = File.ReadAllText(path);
        return Task.FromResult(JsonSerializer.Deserialize<TickImportRecord>(json, JsonOpts));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TickImportRecord>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<TickImportRecord>();
        if (!Directory.Exists(_baseDir))
            return Task.FromResult<IReadOnlyList<TickImportRecord>>(results);

        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<TickImportRecord>(json, JsonOpts);
                if (record is not null) results.Add(record);
            }
            catch { /* skip malformed files */ }
        }

        return Task.FromResult<IReadOnlyList<TickImportRecord>>(results);
    }

    /// <inheritdoc/>
    public Task SaveAsync(TickImportRecord record, CancellationToken ct = default)
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
