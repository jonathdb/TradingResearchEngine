namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Result of validating exported strategy code for structural correctness.
/// </summary>
/// <param name="IsValid">Whether the exported code passed all structural validation checks.</param>
/// <param name="Errors">Specific validation errors found during structural analysis.</param>
public sealed record ExportValidationResult(
    bool IsValid,
    IReadOnlyList<ExportValidationError> Errors)
{
    /// <summary>Creates a successful validation result with no errors.</summary>
    public static ExportValidationResult Success() =>
        new(true, Array.Empty<ExportValidationError>());

    /// <summary>Creates a failed validation result with the specified errors.</summary>
    public static ExportValidationResult Failure(IReadOnlyList<ExportValidationError> errors) =>
        new(false, errors);
}
