namespace TradingResearchEngine.Application.Export;

/// <summary>
/// A specific validation error found during export code structural analysis.
/// </summary>
/// <param name="Line">The line number where the error was detected, or null if not line-specific.</param>
/// <param name="Section">The section or structural element related to the error (e.g., "braces", "version directive").</param>
/// <param name="Message">A human-readable description of the validation error.</param>
public sealed record ExportValidationError(
    int? Line,
    string Section,
    string Message);
