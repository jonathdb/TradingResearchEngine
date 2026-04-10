using System.Text.Json;
using TradingResearchEngine.Application.DataFiles;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based implementation of <see cref="IDataFileRepository"/>.
/// Stores each record as datafiles/{fileId}.json.
/// </summary>
public sealed class JsonDataFileRepository : IDataFileRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseDir;

    /// <inheritdoc cref="JsonDataFileRepository"/>
    public JsonDataFileRepository(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public Task<DataFileRecord?> GetAsync(string fileId, CancellationToken ct = default)
    {
        var path = GetPath(fileId);
        if (!File.Exists(path)) return Task.FromResult<DataFileRecord?>(null);
        var json = File.ReadAllText(path);
        return Task.FromResult(JsonSerializer.Deserialize<DataFileRecord>(json, JsonOpts));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DataFileRecord>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<DataFileRecord>();
        if (!Directory.Exists(_baseDir))
            return Task.FromResult<IReadOnlyList<DataFileRecord>>(results);

        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<DataFileRecord>(json, JsonOpts);
                if (record is not null) results.Add(record);
            }
            catch { /* skip malformed files */ }
        }
        return Task.FromResult<IReadOnlyList<DataFileRecord>>(results);
    }

    /// <inheritdoc/>
    public Task SaveAsync(DataFileRecord record, CancellationToken ct = default)
    {
        var path = GetPath(record.FileId);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(path, json);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string fileId, CancellationToken ct = default)
    {
        var path = GetPath(fileId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string fileId) => Path.Combine(_baseDir, $"{fileId}.json");
}
