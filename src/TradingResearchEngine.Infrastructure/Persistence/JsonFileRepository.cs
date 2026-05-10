using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// Persists entities as individual JSON files named {id}.json in a configurable directory.
/// </summary>
public sealed class JsonFileRepository<T> : IRepository<T> where T : IHasId
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <inheritdoc cref="JsonFileRepository{T}"/>
    public JsonFileRepository(IOptions<RepositoryOptions> options)
    {
        _baseDir = string.IsNullOrWhiteSpace(options.Value.BaseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", typeof(T).Name)
            : options.Value.BaseDirectory;
        if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(T entity, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{entity.Id}.json");
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{id}.json");
        if (!File.Exists(path)) return default;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<T>();
        if (!Directory.Exists(_baseDir)) return results;
        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (entity is not null) results.Add(entity);
        }
        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<T>> ListRecentAsync(int count, CancellationToken ct = default)
    {
        if (!Directory.Exists(_baseDir)) return Array.Empty<T>();

        // Order by file last-write time descending, take only the most recent N files
        var files = new DirectoryInfo(_baseDir)
            .GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(count);

        var results = new List<T>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file.FullName, ct);
            var entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (entity is not null) results.Add(entity);
        }
        return results;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
