namespace TradingResearchEngine.Application.TickImport;

/// <summary>Progress update from a running tick import.</summary>
public sealed record TickImportProgressUpdate(
    string ImportId,
    int Current,
    int Total,
    string Label);

/// <summary>Completion notification from a finished tick import.</summary>
public sealed record TickImportCompletionUpdate(
    string ImportId,
    TickImportStatus Status,
    string? ErrorMessage);

/// <summary>Snapshot of the currently active tick import.</summary>
public sealed record ActiveTickImport(
    string ImportId,
    string Symbol,
    int Current,
    int Total,
    DateTimeOffset StartedAt);
