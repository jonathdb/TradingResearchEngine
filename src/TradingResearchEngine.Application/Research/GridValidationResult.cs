namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Result of validating a <see cref="CompositeParameterGrid"/> against a composite strategy configuration.
/// </summary>
/// <param name="IsValid">Whether the grid passed all validation checks.</param>
/// <param name="Errors">Validation errors found during grid analysis.</param>
public sealed record GridValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors)
{
    /// <summary>Creates a successful validation result with no errors.</summary>
    public static GridValidationResult Success() =>
        new(true, Array.Empty<string>());

    /// <summary>Creates a failed validation result with the specified errors.</summary>
    public static GridValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, errors);

    /// <summary>Creates a failed validation result with a single error.</summary>
    public static GridValidationResult Failure(string error) =>
        new(false, new[] { error });
}
