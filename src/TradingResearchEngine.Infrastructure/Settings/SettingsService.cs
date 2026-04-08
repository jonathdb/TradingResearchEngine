using System.Text.Json;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Infrastructure.Settings;

/// <summary>
/// Reads and writes application settings from a JSON file.
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>Loads settings from disk, or returns defaults if file doesn't exist.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath)) return AppSettings.Default;
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    /// <summary>Saves settings to disk.</summary>
    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(_settingsPath, json);
    }
}

/// <summary>Application-wide settings.</summary>
public sealed record AppSettings(
    string DataDirectory,
    string ExportDirectory,
    ExecutionRealismProfile DefaultRealismProfile,
    decimal DefaultInitialCash,
    decimal DefaultRiskFreeRate,
    string DefaultSizingPolicy)
{
    /// <summary>Default settings.</summary>
    public static AppSettings Default { get; } = new(
        "data",
        "exports",
        ExecutionRealismProfile.StandardBacktest,
        100_000m,
        0.02m,
        "PercentEquity");
}
