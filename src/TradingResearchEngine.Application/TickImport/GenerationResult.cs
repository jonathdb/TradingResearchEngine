namespace TradingResearchEngine.Application.TickImport;

/// <summary>Result of a timeframe generation operation.</summary>
public sealed record GenerationResult(
    string OutputFilePath,
    string OutputFileId,
    int BarCount,
    DateTimeOffset FirstBar,
    DateTimeOffset LastBar);
