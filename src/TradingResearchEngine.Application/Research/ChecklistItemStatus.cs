namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Status of a quantitative checklist item that requires metric evaluation
/// rather than simple boolean completion.
/// </summary>
public enum ChecklistOutcome
{
    /// <summary>The metric meets or exceeds the required threshold.</summary>
    Passed,

    /// <summary>The metric is below the required threshold.</summary>
    Failed,

    /// <summary>The metric has not been computed yet (null or insufficient data).</summary>
    Incomplete
}

/// <summary>
/// Represents the evaluation result of a quantitative checklist item,
/// including the outcome and a human-readable message.
/// </summary>
/// <param name="Outcome">The evaluation outcome (Passed, Failed, or Incomplete).</param>
/// <param name="Message">Human-readable message describing the result, including actual values and thresholds when applicable.</param>
public sealed record ChecklistItemStatus(
    ChecklistOutcome Outcome,
    string Message)
{
    /// <summary>Creates a Passed status with an optional message.</summary>
    public static ChecklistItemStatus Passed(string message = "Passed") =>
        new(ChecklistOutcome.Passed, message);

    /// <summary>Creates a Failed status with a descriptive message.</summary>
    public static ChecklistItemStatus Failed(string message) =>
        new(ChecklistOutcome.Failed, message);

    /// <summary>Creates an Incomplete status with a descriptive message.</summary>
    public static ChecklistItemStatus Incomplete(string message) =>
        new(ChecklistOutcome.Incomplete, message);

    /// <summary>Whether this item counts as complete (Passed).</summary>
    public bool IsComplete => Outcome == ChecklistOutcome.Passed;
}
