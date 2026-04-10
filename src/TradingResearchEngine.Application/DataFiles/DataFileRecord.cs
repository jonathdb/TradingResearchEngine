using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.DataFiles;

/// <summary>
/// Metadata for a registered CSV data file. Persisted as JSON.
/// </summary>
public sealed record DataFileRecord(
    string FileId,
    string FileName,
    string FilePath,
    string? DetectedSymbol,
    string? DetectedTimeframe,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    int BarCount,
    ValidationStatus ValidationStatus,
    string? ValidationError,
    DateTimeOffset AddedAt) : IHasId
{
    /// <inheritdoc/>
    public string Id => FileId;
}

/// <summary>Validation state of a data file.</summary>
public enum ValidationStatus
{
    /// <summary>File added but not yet validated.</summary>
    Pending,

    /// <summary>File passed all validation rules.</summary>
    Valid,

    /// <summary>File failed one or more validation rules.</summary>
    Invalid
}
