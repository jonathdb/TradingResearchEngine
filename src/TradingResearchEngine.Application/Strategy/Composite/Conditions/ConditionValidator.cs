namespace TradingResearchEngine.Application.Strategy.Composite.Conditions;

/// <summary>
/// Validates that all indicator references in a condition AST are present in the defined indicator IDs.
/// Walks the AST recursively collecting all <see cref="IndicatorRefNode"/> references and compares
/// them against the defined set using case-insensitive comparison.
/// </summary>
public static class ConditionValidator
{
    /// <summary>
    /// Validates that all indicator references in the AST are present in the defined indicator IDs.
    /// </summary>
    /// <param name="ast">The parsed condition AST to validate.</param>
    /// <param name="definedIndicatorIds">The set of indicator IDs defined in the strategy config.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ast"/> or <paramref name="definedIndicatorIds"/> is null.</exception>
    /// <exception cref="ConditionValidationException">Thrown when undefined indicator references are found.</exception>
    public static void Validate(ConditionNode ast, IReadOnlyList<string> definedIndicatorIds)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(definedIndicatorIds);

        var referencedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectIndicatorReferences(ast, referencedIds);

        var definedSet = new HashSet<string>(definedIndicatorIds, StringComparer.OrdinalIgnoreCase);
        var undefinedIds = new List<string>();

        foreach (var id in referencedIds)
        {
            if (!definedSet.Contains(id))
            {
                undefinedIds.Add(id);
            }
        }

        if (undefinedIds.Count > 0)
        {
            throw new ConditionValidationException(undefinedIds, definedIndicatorIds.ToList());
        }
    }

    /// <summary>
    /// Recursively walks the AST and collects all indicator IDs from <see cref="IndicatorRefNode"/> instances.
    /// </summary>
    /// <param name="node">The current AST node to inspect.</param>
    /// <param name="indicatorIds">The set to accumulate discovered indicator IDs into.</param>
    private static void CollectIndicatorReferences(ConditionNode node, HashSet<string> indicatorIds)
    {
        switch (node)
        {
            case LogicalNode logical:
                CollectIndicatorReferences(logical.Left, indicatorIds);
                CollectIndicatorReferences(logical.Right, indicatorIds);
                break;

            case ComparisonNode comparison:
                CollectValueReferences(comparison.Left, indicatorIds);
                CollectValueReferences(comparison.Right, indicatorIds);
                break;

            case CrossNode cross:
                CollectValueReferences(cross.Left, indicatorIds);
                CollectValueReferences(cross.Right, indicatorIds);
                break;
        }
    }

    /// <summary>
    /// Collects indicator IDs from a <see cref="ValueNode"/>.
    /// Only <see cref="IndicatorRefNode"/> instances contribute references;
    /// <see cref="PriceRefNode"/> and <see cref="LiteralNode"/> are ignored.
    /// </summary>
    /// <param name="node">The value node to inspect.</param>
    /// <param name="indicatorIds">The set to accumulate discovered indicator IDs into.</param>
    private static void CollectValueReferences(ValueNode node, HashSet<string> indicatorIds)
    {
        if (node is IndicatorRefNode indicatorRef)
        {
            indicatorIds.Add(indicatorRef.IndicatorId);
        }
        // PriceRefNode and LiteralNode don't need validation
    }
}
