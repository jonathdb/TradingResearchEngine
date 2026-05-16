using System.Text.Json;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based research journal repository. Entries: journal/{entryId}.json
/// </summary>
public sealed class JsonResearchJournalRepository : IResearchJournalRepository
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonResearchJournalRepository(string baseDir)
    {
        _baseDir = baseDir;
        if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResearchJournalEntry>> ListByStrategyAsync(
        string strategyId, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(ct);
        return all.Where(e => e.StrategyId == strategyId)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResearchJournalEntry>> ListByDateRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(ct);
        return all.Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ResearchJournalEntry entry, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{entry.EntryId}.json");
        var json = JsonSerializer.Serialize(entry, JsonOpts);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private async Task<List<ResearchJournalEntry>> LoadAllAsync(CancellationToken ct)
    {
        var results = new List<ResearchJournalEntry>();
        if (!Directory.Exists(_baseDir)) return results;
        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(file, ct);
            var entity = JsonSerializer.Deserialize<ResearchJournalEntry>(json, JsonOpts);
            if (entity is not null) results.Add(entity);
        }
        return results;
    }
}
