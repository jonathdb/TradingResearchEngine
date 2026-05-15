using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Enumerates the possible outcomes of a final validation attempt.
/// </summary>
public enum FinalValidationStatus
{
    /// <summary>Final validation completed successfully.</summary>
    Success,

    /// <summary>User declined the confirmation prompt.</summary>
    Cancelled,

    /// <summary>The test set was already consumed for this strategy version.</summary>
    AlreadyConsumed,

    /// <summary>Validation failed due to configuration or data errors.</summary>
    Failed
}

/// <summary>
/// Result of a final validation attempt, encapsulating the outcome status,
/// an optional backtest result on success, a human-readable message,
/// and optional checklist warnings surfaced during the validation flow.
/// </summary>
/// <param name="Status">The outcome of the final validation attempt.</param>
/// <param name="Message">A human-readable explanation of the outcome.</param>
/// <param name="Result">The backtest result when validation succeeds; null otherwise.</param>
/// <param name="ChecklistWarnings">Warnings from the research checklist when critical items are incomplete.</param>
public sealed record FinalValidationResult(
    FinalValidationStatus Status,
    string Message,
    BacktestResult? Result = null,
    IReadOnlyList<string>? ChecklistWarnings = null)
{
    /// <summary>
    /// Creates a result indicating the user declined confirmation.
    /// </summary>
    /// <param name="message">Explanation of the cancellation.</param>
    public static FinalValidationResult Cancelled(string message) =>
        new(FinalValidationStatus.Cancelled, message);

    /// <summary>
    /// Creates a result indicating the test set was already consumed.
    /// </summary>
    /// <param name="message">Explanation that the test set is no longer available.</param>
    public static FinalValidationResult AlreadyConsumed(string message) =>
        new(FinalValidationStatus.AlreadyConsumed, message);

    /// <summary>
    /// Creates a successful result containing the backtest output.
    /// </summary>
    /// <param name="result">The completed backtest result.</param>
    public static FinalValidationResult Success(BacktestResult result) =>
        new(FinalValidationStatus.Success, "Final validation completed successfully.", result);

    /// <summary>
    /// Creates a failure result with validation error details.
    /// </summary>
    /// <param name="message">Description of the failure.</param>
    public static FinalValidationResult Failed(string message) =>
        new(FinalValidationStatus.Failed, message);

    /// <summary>
    /// Creates a successful result with checklist warnings attached.
    /// </summary>
    /// <param name="result">The completed backtest result.</param>
    /// <param name="warnings">Checklist warnings surfaced during validation.</param>
    public static FinalValidationResult SuccessWithWarnings(BacktestResult result, IReadOnlyList<string> warnings) =>
        new(FinalValidationStatus.Success, "Final validation completed successfully.", result, warnings);
}
