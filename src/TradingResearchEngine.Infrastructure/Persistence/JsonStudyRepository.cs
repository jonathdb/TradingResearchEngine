using System.Text.Json;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based study repository. Studies: studies/{studyId}.json
/// </summary>
public sealed class JsonStudyRepository : IStudyRepository
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonStudyRepository(string baseDir)
    {
        _baseDir = baseDir;
        if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
    }

    public async Task<StudyRecord?> GetAsync(string studyId, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{studyId}.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<StudyRecord>(json, JsonOpts);
    }

    public async Task<IReadOnlyList<StudyRecord>> ListByVersionAsync(string strategyVersionId, CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        return all.Where(s => s.StrategyVersionId == strategyVersionId).ToList();
    }

    public async Task<IReadOnlyList<StudyRecord>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<StudyRecord>();
        if (!Directory.Exists(_baseDir)) return results;
        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entity = JsonSerializer.Deserialize<StudyRecord>(json, JsonOpts);
            if (entity is not null) results.Add(entity);
        }
        return results.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task SaveAsync(StudyRecord study, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{study.StudyId}.json");
        var json = JsonSerializer.Serialize(study, JsonOpts);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public Task DeleteAsync(string studyId, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{studyId}.json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SaveResultAsync(string studyId, string resultJson, CancellationToken ct = default)
    {
        var study = await GetAsync(studyId, ct);
        if (study is null) return;
        await SaveAsync(study with { ResultJson = resultJson }, ct);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<StudyRecord>> ListPagedAsync(
        int page,
        int pageSize,
        StudyType? typeFilter = null,
        StudyStatus? statusFilter = null,
        string? strategyVersionId = null,
        CancellationToken ct = default)
    {
        // Load all studies (in-memory filtering for JSON store).
        // Safe for up to ~5000 records. Beyond that, extend to SQLite index.
        var all = await ListAsync(ct);

        IEnumerable<StudyRecord> filtered = all;

        if (typeFilter is { } type)
            filtered = filtered.Where(s => s.Type == type);

        if (statusFilter is { } status)
            filtered = filtered.Where(s => s.Status == status);

        if (!string.IsNullOrEmpty(strategyVersionId))
            filtered = filtered.Where(s => s.StrategyVersionId == strategyVersionId);

        var materialised = filtered.ToList();
        var totalCount = materialised.Count;

        var items = materialised
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<StudyRecord>(items, totalCount, page, pageSize);
    }
}
