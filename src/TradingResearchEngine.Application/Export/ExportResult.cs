namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Result of a strategy export operation containing the generated source code,
/// target format metadata, and any translation warnings.
/// </summary>
/// <param name="Format">The export format that was used.</param>
/// <param name="FileName">Suggested filename including the appropriate extension (.mq4, .mq5, .pine).</param>
/// <param name="Code">Generated platform-specific source code. Empty when the strategy type is unsupported.</param>
/// <param name="Warnings">Translation warnings where exact equivalence is impossible, or unsupported type notices.</param>
public sealed record ExportResult(
    ExportFormat Format,
    string FileName,
    string Code,
    IReadOnlyList<string> Warnings);
