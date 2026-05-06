using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Converts a validated <see cref="StrategyVersion"/> into equivalent source code
/// for an external trading platform. Each implementation handles a single
/// <see cref="ExportFormat"/>.
/// </summary>
public interface IStrategyExporter
{
    /// <summary>The export format this exporter handles.</summary>
    ExportFormat Format { get; }

    /// <summary>
    /// Generates platform-specific source code for the given strategy version.
    /// Returns an <see cref="ExportResult"/> with empty <c>Code</c> and a warning
    /// when the strategy type is unsupported.
    /// </summary>
    /// <param name="version">The strategy version to export.</param>
    /// <param name="ct">Cancellation token propagated to async operations.</param>
    /// <returns>An <see cref="ExportResult"/> containing the generated code and any warnings.</returns>
    Task<ExportResult> ExportAsync(StrategyVersion version, CancellationToken ct);
}
