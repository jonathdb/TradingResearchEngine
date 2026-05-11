using System.Globalization;
using System.Text;

namespace TradingResearchEngine.Application.Strategies.Composite.Conditions;

/// <summary>
/// Formats a condition expression AST back into a canonical condition expression string.
/// Used for round-trip validation and export. Emits parentheses only where needed
/// for precedence clarity (OR inside AND requires parentheses).
/// </summary>
public static class ConditionPrettyPrinter
{
    /// <summary>
    /// Prints a condition AST node into its canonical string representation.
    /// </summary>
    /// <param name="ast">The root AST node to print.</param>
    /// <returns>A canonical condition expression string that can be re-parsed to produce an equivalent AST.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ast"/> is null.</exception>
    public static string Print(ConditionNode ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var sb = new StringBuilder();
        PrintNode(sb, ast, parentOperator: null);
        return sb.ToString();
    }

    private static void PrintNode(StringBuilder sb, ConditionNode node, LogicalOperator? parentOperator)
    {
        switch (node)
        {
            case LogicalNode logical:
                PrintLogical(sb, logical, parentOperator);
                break;
            case ComparisonNode comparison:
                PrintComparison(sb, comparison);
                break;
            case CrossNode cross:
                PrintCross(sb, cross);
                break;
            default:
                throw new InvalidOperationException($"Unknown condition node type: {node.GetType().Name}");
        }
    }

    private static void PrintLogical(StringBuilder sb, LogicalNode node, LogicalOperator? parentOperator)
    {
        // Emit parentheses when an OR node is a child of an AND node
        var needsParens = parentOperator == LogicalOperator.And && node.Operator == LogicalOperator.Or;

        if (needsParens)
            sb.Append('(');

        PrintNode(sb, node.Left, node.Operator);
        sb.Append(' ');
        sb.Append(FormatLogicalOperator(node.Operator));
        sb.Append(' ');
        PrintNode(sb, node.Right, node.Operator);

        if (needsParens)
            sb.Append(')');
    }

    private static void PrintComparison(StringBuilder sb, ComparisonNode node)
    {
        PrintValue(sb, node.Left);
        sb.Append(' ');
        sb.Append(FormatComparisonOperator(node.Operator));
        sb.Append(' ');
        PrintValue(sb, node.Right);
    }

    private static void PrintCross(StringBuilder sb, CrossNode node)
    {
        sb.Append(node.Direction == CrossDirection.Above ? "crosses_above(" : "crosses_below(");
        PrintValue(sb, node.Left);
        sb.Append(", ");
        PrintValue(sb, node.Right);
        sb.Append(')');
    }

    private static void PrintValue(StringBuilder sb, ValueNode node)
    {
        switch (node)
        {
            case IndicatorRefNode indicator:
                sb.Append(indicator.IndicatorId);
                if (indicator.SubProperty is not null)
                {
                    sb.Append('.');
                    sb.Append(indicator.SubProperty);
                }
                break;
            case PriceRefNode price:
                sb.Append(FormatPriceField(price.Field));
                break;
            case LiteralNode literal:
                sb.Append(literal.Value.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException($"Unknown value node type: {node.GetType().Name}");
        }
    }

    private static string FormatLogicalOperator(LogicalOperator op) => op switch
    {
        LogicalOperator.And => "AND",
        LogicalOperator.Or => "OR",
        _ => throw new InvalidOperationException($"Unknown logical operator: {op}")
    };

    private static string FormatComparisonOperator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThanOrEqual => "<=",
        ComparisonOperator.Equal => "==",
        ComparisonOperator.NotEqual => "!=",
        _ => throw new InvalidOperationException($"Unknown comparison operator: {op}")
    };

    private static string FormatPriceField(PriceField field) => field switch
    {
        PriceField.Open => "open",
        PriceField.High => "high",
        PriceField.Low => "low",
        PriceField.Close => "close",
        PriceField.Volume => "volume",
        _ => throw new InvalidOperationException($"Unknown price field: {field}")
    };
}
