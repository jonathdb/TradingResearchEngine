using System.Text.Json;
using TradingResearchEngine.Application.PropFirm;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON file-based persistence for prop-firm evaluation records.
/// Follows the standard JsonFileRepository pattern.
/// </summary>
public sealed class JsonPropFirmEvaluationRepository : IPropFirmEvaluationRepository
{
    private readonly string _baseDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <inheritdoc cref="JsonPropFirmEvaluationRepository"/>
    public JsonPropFirmEvaluationRepository(string baseDir)
    {
        _baseDir = baseDir;
        if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
    }

    /// <inheritdoc/>
    public async Task<bool> HasCompletedEvaluationAsync(string strategyVersionId, CancellationToken ct = default)
    {
        if (!Directory.Exists(_baseDir)) return false;

        foreach (var file in Directory.GetFiles(_baseDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(file, ct);
            var record = JsonSerializer.Deserialize<PropFirmEvaluationRecord>(json, JsonOptions);
            if (record is not null && record.StrategyVersionId == strategyVersionId)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task SaveEvaluationAsync(string strategyVersionId, PropFirmEvaluationRecord record, CancellationToken ct = default)
    {
        var path = Path.Combine(_baseDir, $"{record.Id}.json");
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
