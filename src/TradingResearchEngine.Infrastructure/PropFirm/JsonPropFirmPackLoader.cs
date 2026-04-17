using System.Text.Json;
using TradingResearchEngine.Application.PropFirm;

namespace TradingResearchEngine.Infrastructure.PropFirm;

/// <summary>
/// Loads prop-firm rule packs from JSON files in the data/firms/ directory.
/// Registered as a singleton in DI.
/// </summary>
public sealed class JsonPropFirmPackLoader : IPropFirmPackLoader
{
    private readonly string _firmsDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc cref="JsonPropFirmPackLoader"/>
    public JsonPropFirmPackLoader(string firmsDir)
    {
        _firmsDir = firmsDir;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PropFirmRulePack>> LoadAllPacksAsync(CancellationToken ct = default)
    {
        var packs = new List<PropFirmRulePack>();
        if (!Directory.Exists(_firmsDir)) return packs;

        foreach (var file in Directory.GetFiles(_firmsDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var pack = JsonSerializer.Deserialize<PropFirmRulePack>(json, JsonOptions);
                if (pack is not null) packs.Add(pack);
            }
            catch (JsonException) { /* skip invalid files */ }
        }

        return packs.OrderBy(p => p.FirmName).ThenBy(p => p.ChallengeName).ToList();
    }

    /// <inheritdoc/>
    public async Task<PropFirmRulePack?> LoadPackAsync(string firmId, CancellationToken ct = default)
    {
        var all = await LoadAllPacksAsync(ct);
        return all.FirstOrDefault(p => p.RulePackId == firmId);
    }
}
