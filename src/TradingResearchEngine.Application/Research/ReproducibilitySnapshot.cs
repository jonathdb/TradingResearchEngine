using TradingResearchEngine.Application.Engine;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Reproducibility snapshot attached to a <see cref="BacktestJob"/>, containing all
/// information needed to reproduce the exact run: resolved configuration, data identity,
/// random seed, engine version, and preset identifier.
/// </summary>
/// <param name="ResolvedConfig">The fully resolved configuration used for the run.</param>
/// <param name="DataFileIdentity">Data file identifier (filename, hash, or last-modified timestamp).</param>
/// <param name="RandomSeed">Explicit random seed used, if any.</param>
/// <param name="EngineVersion">Engine version string at the time of execution.</param>
/// <param name="PresetId">Configuration preset identifier applied, if any.</param>
public sealed record ReproducibilitySnapshot(
    ResolvedConfig ResolvedConfig,
    string DataFileIdentity,
    int? RandomSeed,
    string EngineVersion,
    string? PresetId);
