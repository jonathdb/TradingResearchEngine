using System.Text.Json;
using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based strategy repository.
/// Strategies: strategies/{strategyId}.json
/// Versions: strategies/{strategyId}/versions/{versionId}.json
/// </summary>
public sealed class JsonStrategyRepository : IStrategyRepository
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonStrategyRepository(string baseDir)
    {
        _baseDir = baseDir;
        if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
    }

    public async Task<StrategyIdentity?> GetAsync(string strategyId, CancellationToken ct = default)
    {
        var path = StrategyPath(strategyId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<StrategyIdentity>(json, JsonOpts);
    }

    public async Task<IReadOnlyList<StrategyIdentity>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<StrategyIdentity>();
        if (!Directory.Exists(_baseDir)) return results;
        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entity = JsonSerializer.Deserialize<StrategyIdentity>(json, JsonOpts);
            if (entity is not null) results.Add(entity);
        }
        return results.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task SaveAsync(StrategyIdentity strategy, CancellationToken ct = default)
    {
        var path = StrategyPath(strategy.StrategyId);
        var json = JsonSerializer.Serialize(strategy, JsonOpts);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public Task DeleteAsync(string strategyId, CancellationToken ct = default)
    {
        var path = StrategyPath(strategyId);
        if (File.Exists(path)) File.Delete(path);
        var versionDir = VersionDir(strategyId);
        if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StrategyVersion>> GetVersionsAsync(string strategyId, CancellationToken ct = default)
    {
        var dir = VersionDir(strategyId);
        var results = new List<StrategyVersion>();
        if (!Directory.Exists(dir)) return results;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entity = JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);
            if (entity is not null) results.Add(entity);
        }
        return results.OrderBy(v => v.VersionNumber).ToList();
    }

    public async Task SaveVersionAsync(StrategyVersion version, CancellationToken ct = default)
    {
        var dir = VersionDir(version.StrategyId);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{version.StrategyVersionId}.json");
        var json = JsonSerializer.Serialize(version, JsonOpts);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<StrategyVersion?> GetLatestVersionAsync(string strategyId, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(strategyId, ct);
        return versions.Count > 0 ? versions[^1] : null;
    }

    private string StrategyPath(string id) => Path.Combine(_baseDir, $"{id}.json");
    private string VersionDir(string strategyId) => Path.Combine(_baseDir, strategyId, "versions");
}
