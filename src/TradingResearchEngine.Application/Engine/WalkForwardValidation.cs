namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Result of walk-forward pre-run validation. Carries validity status, expected window count,
/// and any error or warning messages produced during validation.
/// </summary>
public sealed record WalkForwardValidation
{
    /// <summary>Whether the walk-forward configuration is valid for execution.</summary>
    public bool IsValid { get; }

    /// <summary>Expected number of walk-forward windows, or null when validation failed before computation.</summary>
    public int? WindowCount { get; }

    /// <summary>Structured error message when validation fails (data insufficient, invalid config).</summary>
    public string? ErrorMessage { get; }

    /// <summary>Warning message for configurations that are valid but may produce limited results.</summary>
    public string? WarningMessage { get; }

    private WalkForwardValidation(bool isValid, int? windowCount, string? errorMessage, string? warningMessage)
    {
        IsValid = isValid;
        WindowCount = windowCount;
        ErrorMessage = errorMessage;
        WarningMessage = warningMessage;
    }

    /// <summary>
    /// Creates a failed validation result indicating the configuration cannot produce valid windows.
    /// </summary>
    /// <param name="errorMessage">Structured error describing why validation failed.</param>
    /// <returns>A <see cref="WalkForwardValidation"/> with <see cref="IsValid"/> = false.</returns>
    public static WalkForwardValidation Fail(string errorMessage)
        => new(isValid: false, windowCount: null, errorMessage: errorMessage, warningMessage: null);

    /// <summary>
    /// Creates a valid-but-warned validation result indicating limited statistical significance.
    /// </summary>
    /// <param name="windowCount">The computed window count (typically 1).</param>
    /// <param name="warningMessage">Warning describing the limitation.</param>
    /// <returns>A <see cref="WalkForwardValidation"/> with <see cref="IsValid"/> = true and a warning.</returns>
    public static WalkForwardValidation Warn(int windowCount, string warningMessage)
        => new(isValid: true, windowCount: windowCount, errorMessage: null, warningMessage: warningMessage);

    /// <summary>
    /// Creates a fully valid validation result with the expected window count.
    /// </summary>
    /// <param name="windowCount">The computed number of walk-forward windows.</param>
    /// <returns>A <see cref="WalkForwardValidation"/> with <see cref="IsValid"/> = true and no warnings.</returns>
    public static WalkForwardValidation Ok(int windowCount)
        => new(isValid: true, windowCount: windowCount, errorMessage: null, warningMessage: null);
}
