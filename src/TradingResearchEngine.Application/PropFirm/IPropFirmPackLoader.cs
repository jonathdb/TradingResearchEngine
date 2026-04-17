namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Loads prop-firm rule packs from persistent storage.
/// Replaces inline LoadBuiltInPacks() calls in Blazor pages.
/// </summary>
public interface IPropFirmPackLoader
{
    /// <summary>Loads all available prop-firm rule packs.</summary>
    Task<IReadOnlyList<PropFirmRulePack>> LoadAllPacksAsync(CancellationToken ct = default);

    /// <summary>Loads a specific prop-firm rule pack by firm ID, or null if not found.</summary>
    Task<PropFirmRulePack?> LoadPackAsync(string firmId, CancellationToken ct = default);
}
