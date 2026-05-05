using System.Text.Json;
using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based strategy repository.
/// Strategies: strategies/{strategyId}.json
/// Versions: strategies/{strategyId}/versions/{versionId}.json
/// Version index: strategies/_version_index/{versionId}.txt → strategyId (for O(1) lookup)
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

    public async Task<IReadOnlyList<StrategyVersion>> GetVersionsAsync(
        string strategyId, CancellationToken ct = default)
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

        // Write flat version index entry for O(1) GetVersionAsync lookups
        var indexDir = Path.Combine(_baseDir, "_version_index");
        if (!Directory.Exists(indexDir)) Directory.CreateDirectory(indexDir);
        await File.WriteAllTextAsync(
            Path.Combine(indexDir, $"{version.StrategyVersionId}.txt"),
            version.StrategyId, ct);
    }

    public async Task<StrategyVersion?> GetLatestVersionAsync(
        string strategyId, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(strategyId, ct);
        return versions.Count > 0 ? versions[^1] : null;
    }

    public async Task<StrategyVersion?> GetVersionAsync(
        string strategyVersionId, CancellationToken ct = default)
    {
        // Fast path: flat index gives us the strategyId directly
        var indexFile = Path.Combine(_baseDir, "_version_index", $"{strategyVersionId}.txt");
        if (File.Exists(indexFile))
        {
            var strategyId = await File.ReadAllTextAsync(indexFile, ct);
            var versionFile = Path.Combine(VersionDir(strategyId), $"{strategyVersionId}.json");
            if (File.Exists(versionFile))
            {
                var json = await File.ReadAllTextAsync(versionFile, ct);
                return JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);
            }
        }

        // Fallback: directory walk for versions saved before this fix
        if (!Directory.Exists(_baseDir)) return null;
        foreach (var strategyDir in Directory.GetDirectories(_baseDir))
        {
            if (Path.GetFileName(strategyDir) == "_version_index") continue;
            var versionFile = Path.Combine(strategyDir, "versions", $"{strategyVersionId}.json");
            if (File.Exists(versionFile))
            {
                var json = await File.ReadAllTextAsync(versionFile, ct);
                var entity = JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);
                // Back-fill the index so next lookup is instant
                if (entity is not null)
                {
                    var idxDir = Path.Combine(_baseDir, "_version_index");
                    if (!Directory.Exists(idxDir)) Directory.CreateDirectory(idxDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(idxDir, $"{strategyVersionId}.txt"),
                        entity.StrategyId, ct);
                }
                return entity;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, int>> GetVersionCountsAsync(
        IEnumerable<string> strategyIds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var strategyIdSet = strategyIds.ToHashSet();
        var counts = strategyIdSet.ToDictionary(id => id, _ => 0);

        // Single I/O pass: enumerate all strategy subdirectories and count version files
        if (Directory.Exists(_baseDir))
        {
            foreach (var strategyDir in Directory.GetDirectories(_baseDir))
            {
                ct.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(strategyDir);
                if (dirName == "_version_index") continue;
                if (!strategyIdSet.Contains(dirName)) continue;

                var versionsDir = Path.Combine(strategyDir, "versions");
                if (Directory.Exists(versionsDir))
                {
                    counts[dirName] = Directory.GetFiles(versionsDir, "*.json").Length;
                }
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, int>>(counts);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StrategyVersion>> ListAllVersionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<StrategyVersion>();
        if (!Directory.Exists(_baseDir)) return results;

        // Single I/O pass: enumerate all strategy subdirectories and read all version files
        foreach (var strategyDir in Directory.GetDirectories(_baseDir))
        {
            ct.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(strategyDir);
            if (dirName == "_version_index") continue;

            var versionsDir = Path.Combine(strategyDir, "versions");
            if (!Directory.Exists(versionsDir)) continue;

            foreach (var file in Directory.GetFiles(versionsDir, "*.json"))
            {
                ct.ThrowIfCancellationRequested();

                var json = await File.ReadAllTextAsync(file, ct);
                var entity = JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);
                if (entity is not null) results.Add(entity);
            }
        }

        return results
            .OrderBy(v => v.StrategyId)
            .ThenBy(v => v.VersionNumber)
            .ToList();
    }

    private string StrategyPath(string id) => Path.Combine(_baseDir, $"{id}.json");
    private string VersionDir(string strategyId) => Path.Combine(_baseDir, strategyId, "versions");
}
