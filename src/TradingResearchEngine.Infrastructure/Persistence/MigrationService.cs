using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// Runs once on startup to migrate orphaned V2/V2.1 BacktestResult records
/// into the V4 strategy model. Non-destructive: original files are untouched.
/// </summary>
public sealed class MigrationService
{
    private const string LockFileName = "migration_v4.lock";
    private const string ImportedStrategyId = "imported-pre-v4";
    private const string ImportedVersionId = "imported-pre-v4-v0";

    private readonly IRepository<BacktestResult> _resultRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly string _lockDir;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(
        IRepository<BacktestResult> resultRepo,
        IStrategyRepository strategyRepo,
        ILogger<MigrationService> logger,
        string? lockDir = null)
    {
        _resultRepo = resultRepo;
        _strategyRepo = strategyRepo;
        _logger = logger;
        _lockDir = lockDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TradingResearchEngine");
    }

    /// <summary>
    /// Checks for the lock file and runs migration if not already done.
    /// Failure is logged but does not throw.
    /// </summary>
    public async Task MigrateIfNeededAsync(CancellationToken ct = default)
    {
        try
        {
            var lockPath = Path.Combine(_lockDir, LockFileName);
            if (File.Exists(lockPath))
            {
                _logger.LogDebug("Migration lock file found, skipping V4 migration.");
                return;
            }

            var allResults = await _resultRepo.ListAsync(ct);
            var orphaned = allResults
                .Where(r => string.IsNullOrEmpty(r.StrategyVersionId))
                .ToList();

            if (orphaned.Count == 0)
            {
                _logger.LogInformation("No orphaned results found, skipping migration.");
                WriteLockFile(lockPath);
                return;
            }

            _logger.LogInformation("Found {Count} orphaned results, running V4 migration.", orphaned.Count);

            // Create synthetic strategy
            var existing = await _strategyRepo.GetAsync(ImportedStrategyId, ct);
            if (existing is null)
            {
                var strategy = new StrategyIdentity(
                    ImportedStrategyId,
                    "Imported (Pre-V4)",
                    "imported",
                    DateTimeOffset.UtcNow,
                    "Auto-created by V4 migration for pre-existing results.");
                await _strategyRepo.SaveAsync(strategy, ct);
            }

            // Create synthetic version
            var versions = await _strategyRepo.GetVersionsAsync(ImportedStrategyId, ct);
            if (!versions.Any(v => v.StrategyVersionId == ImportedVersionId))
            {
                var dummyConfig = new ScenarioConfig(
                    "imported", "Imported", ReplayMode.Bar, "csv",
                    new Dictionary<string, object>(), "imported",
                    new Dictionary<string, object>(), new Dictionary<string, object>(),
                    "Zero", "Zero", 100_000m, 0.02m, null, null, null, null);

                var version = new StrategyVersion(
                    ImportedVersionId, ImportedStrategyId, 0,
                    new Dictionary<string, object>(), dummyConfig,
                    DateTimeOffset.UtcNow, "v0 (imported)");
                await _strategyRepo.SaveVersionAsync(version, ct);
            }

            // Link orphaned results
            foreach (var result in orphaned)
            {
                var linked = result with { StrategyVersionId = ImportedVersionId };
                await _resultRepo.SaveAsync(linked, ct);
            }

            _logger.LogInformation("V4 migration complete. Linked {Count} results.", orphaned.Count);
            WriteLockFile(lockPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "V4 migration failed. Will retry on next startup.");
            // Do NOT rethrow — migration failure must not crash the app
        }
    }

    private void WriteLockFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"Migrated at {DateTimeOffset.UtcNow:O}");
    }
}
