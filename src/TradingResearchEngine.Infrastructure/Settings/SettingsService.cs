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

/// <summary>
/// Application-wide user settings persisted as JSON.
/// Uses property-based record syntax so new fields deserialise safely from
/// older JSON files (missing keys fall back to their property initialisers).
/// </summary>
public sealed record AppSettings
{
    // ── Storage ──────────────────────────────────────────────────────────
    public string DataDirectory { get; init; } = "data";
    public string ExportDirectory { get; init; } = "exports";
    public string? QdmWatchDirectory { get; init; }
    public string QdmTimezoneId { get; init; } = "UTC";

    // ── Backtest Defaults ─────────────────────────────────────────────
    public ExecutionRealismProfile DefaultRealismProfile { get; init; } = ExecutionRealismProfile.StandardBacktest;
    public decimal DefaultInitialCash { get; init; } = 100_000m;
    public decimal DefaultRiskFreeRate { get; init; } = 0.02m;
    public string DefaultSizingPolicy { get; init; } = "PercentEquity";

    // ── Risk ──────────────────────────────────────────────────────────
    public decimal MaxExposurePercent { get; init; } = 10m;

    // ── Monte Carlo ────────────────────────────────────────────────────
    public int MonteCarloSimulationCount { get; init; } = 1000;
    public int? MonteCarloSeed { get; init; }
    public decimal MonteCarloRuinThreshold { get; init; } = 0.5m;
    public int MonteCarloBlockSize { get; init; } = 1;

    // ── Parameter Sweep ────────────────────────────────────────────────
    /// <summary>0 means use Environment.ProcessorCount at runtime.</summary>
    public int SweepMaxParallelism { get; init; } = 0;

    // ── Reporting ─────────────────────────────────────────────────────
    public int ReportingDecimalPlaces { get; init; } = 2;

    /// <summary>Default settings used when no file exists on disk.</summary>
    public static AppSettings Default => new();
}
