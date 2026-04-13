namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Validates that a <see cref="ConfigDraft"/> has all required fields
/// for its <see cref="ConfigDraft.CurrentStep"/>. Called server-side
/// before every draft persistence operation.
/// </summary>
public static class ConfigDraftValidator
{
    /// <summary>
    /// Returns an empty list when the draft is valid for its current step.
    /// Returns one error string per missing field otherwise.
    /// Validation is cumulative: step N includes all rules from steps &lt; N.
    /// </summary>
    public static IReadOnlyList<string> ValidateStep(ConfigDraft draft)
    {
        var errors = new List<string>();

        if (draft.CurrentStep >= 1 && string.IsNullOrWhiteSpace(draft.StrategyName))
            errors.Add("Step 1 requires StrategyName to be set.");

        if (draft.CurrentStep >= 2)
        {
            if (string.IsNullOrWhiteSpace(draft.StrategyType))
                errors.Add("Step 2 requires StrategyType to be set.");
            if (draft.DataConfig is null)
                errors.Add("Step 2 requires DataConfig.");
        }

        if (draft.CurrentStep >= 3 && (draft.StrategyParameters is null || draft.StrategyParameters.Count == 0))
            errors.Add("Step 3 requires at least one StrategyParameter.");

        if (draft.CurrentStep >= 4)
        {
            if (draft.ExecutionConfig is null)
                errors.Add("Step 4 requires ExecutionConfig.");
            if (draft.RiskConfig is null)
                errors.Add("Step 4 requires RiskConfig.");
        }

        // Step 5 is cumulative — all prior rules already checked above.

        return errors;
    }
}
