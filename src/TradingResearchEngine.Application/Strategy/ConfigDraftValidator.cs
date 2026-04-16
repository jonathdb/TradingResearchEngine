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

    /// <summary>
    /// Returns advisory warnings for a <see cref="ConfigDraft"/> based on its
    /// relationship to the matched <see cref="StrategyTemplate"/>. Unlike
    /// <see cref="ValidateStep"/>, these are non-blocking — the user may proceed
    /// despite warnings. Currently checks for timeframe mismatches between
    /// <c>DataConfig.Timeframe</c> and the template's <c>RecommendedTimeframe</c>.
    /// </summary>
    public static IReadOnlyList<string> ValidateWarnings(ConfigDraft draft, IReadOnlyList<StrategyTemplate> templates)
    {
        if (draft.CurrentStep < 2)
            return Array.Empty<string>();

        if (draft.DataConfig is null)
            return Array.Empty<string>();

        if (draft.TemplateId is null)
            return Array.Empty<string>();

        var template = templates.FirstOrDefault(t =>
            string.Equals(t.TemplateId, draft.TemplateId, StringComparison.OrdinalIgnoreCase));

        if (template is null)
            return Array.Empty<string>();

        if (string.IsNullOrEmpty(draft.DataConfig.Timeframe) ||
            string.Equals(draft.DataConfig.Timeframe, "Any", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        if (string.IsNullOrEmpty(template.RecommendedTimeframe) ||
            string.Equals(template.RecommendedTimeframe, "Any", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        if (!string.Equals(draft.DataConfig.Timeframe, template.RecommendedTimeframe, StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                $"Data timeframe '{draft.DataConfig.Timeframe}' does not match the template's recommended timeframe '{template.RecommendedTimeframe}'. Results may be unexpected."
            };
        }

        return Array.Empty<string>();
    }
}
