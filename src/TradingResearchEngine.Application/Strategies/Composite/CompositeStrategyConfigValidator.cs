using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Application.Strategies.Composite.Conditions;

namespace TradingResearchEngine.Application.Strategies.Composite;

/// <summary>
/// Validates a <see cref="CompositeStrategyConfig"/> before engine execution.
/// Returns all violations found, not just the first.
/// </summary>
public static class CompositeStrategyConfigValidator
{
    /// <summary>
    /// The set of supported indicator type names (case-insensitive).
    /// Includes both hardcoded built-in types and all keys from the Skender indicator catalog.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = BuildSupportedTypes();

    private static HashSet<string> BuildSupportedTypes()
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sma", "ema", "rsi", "macd", "bollinger", "atr", "stochastic", "donchian"
        };

        // Include all keys from the Skender indicator catalog
        foreach (var entry in SkenderIndicatorCatalog.All)
        {
            types.Add(entry.Key);
        }

        return types;
    }

    /// <summary>
    /// Validates the specified composite strategy configuration and returns all violations found.
    /// </summary>
    /// <param name="config">The composite strategy configuration to validate.</param>
    /// <returns>
    /// A read-only list of violation messages. An empty list indicates the configuration is valid.
    /// </returns>
    public static IReadOnlyList<string> Validate(CompositeStrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        var definedIds = ValidateIndicators(config, errors);
        ValidateExpression(config.EntryCondition, "entry", definedIds, errors);
        ValidateExpression(config.ExitCondition, "exit", definedIds, errors);

        return errors;
    }

    /// <summary>
    /// Validates indicator definitions: checks for unique IDs and supported types.
    /// Returns the list of defined indicator IDs for use in expression validation.
    /// </summary>
    private static IReadOnlyList<string> ValidateIndicators(CompositeStrategyConfig config, List<string> errors)
    {
        var definedIds = new List<string>();

        if (config.Indicators is null || config.Indicators.Count == 0)
        {
            errors.Add("At least one indicator must be defined.");
            return definedIds;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indicator in config.Indicators)
        {
            if (string.IsNullOrWhiteSpace(indicator.Id))
            {
                errors.Add("Indicator ID must not be empty.");
                continue;
            }

            if (!seenIds.Add(indicator.Id))
            {
                errors.Add($"Duplicate indicator ID '{indicator.Id}'. Indicator IDs must be unique (case-insensitive).");
            }

            if (string.IsNullOrWhiteSpace(indicator.Type) || !SupportedTypes.Contains(indicator.Type))
            {
                errors.Add(
                    $"Indicator '{indicator.Id}' has unsupported type '{indicator.Type ?? "(null)"}'. " +
                    $"Supported types: {string.Join(", ", SupportedTypes.Order())}.");
            }

            definedIds.Add(indicator.Id);
        }

        return definedIds;
    }

    /// <summary>
    /// Validates a condition expression by attempting to parse it and then validating
    /// that all indicator references are defined.
    /// </summary>
    private static void ValidateExpression(
        string? expression,
        string expressionName,
        IReadOnlyList<string> definedIds,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            errors.Add($"The {expressionName} condition expression must not be empty.");
            return;
        }

        ConditionNode ast;
        try
        {
            ast = ConditionParser.Parse(expression);
        }
        catch (ConditionParseException ex)
        {
            errors.Add($"The {expressionName} condition expression has a syntax error: {ex.Message}");
            return;
        }

        try
        {
            ConditionValidator.Validate(ast, definedIds);
        }
        catch (ConditionValidationException ex)
        {
            errors.Add(
                $"The {expressionName} condition expression references undefined indicators: " +
                $"[{string.Join(", ", ex.UndefinedReferences)}]. " +
                $"Defined indicators: [{string.Join(", ", ex.DefinedIndicatorIds)}].");
        }
    }
}
