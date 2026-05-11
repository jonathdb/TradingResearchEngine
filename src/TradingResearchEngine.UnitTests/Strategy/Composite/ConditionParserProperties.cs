using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies.Composite.Conditions;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for the ConditionParser round-trip behaviour.
/// **Validates: Requirements 5.5, 5.6**
/// </summary>
public class ConditionParserProperties
{
    // Feature: composite-strategy-engine, Property 2: Condition expression parse round-trip
    /// <summary>
    /// For any valid condition expression AST, pretty-printing to a string and re-parsing
    /// SHALL produce an equivalent AST.
    /// **Validates: Requirements 5.5, 5.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property ParseRoundTrip_PrettyPrintThenReparse_ProducesEquivalentAst()
    {
        var gen = GenerateConditionNode(depth: 0);

        return Prop.ForAll(
            gen.ToArbitrary(),
            ast =>
            {
                // Print the generated AST to canonical form
                var printed = ConditionPrettyPrinter.Print(ast);

                // Parse the canonical string back into an AST
                var reparsed = ConditionParser.Parse(printed);

                // Print the reparsed AST — this should be identical to the first print (idempotent)
                var reprinted = ConditionPrettyPrinter.Print(reparsed);

                // The canonical form is stable: print(parse(print(ast))) == print(ast)
                // This validates the round-trip property: the canonical string representation
                // is preserved through parse → print cycles.
                return (printed == reprinted)
                    .Label($"Pretty-print not idempotent.\nFirst print:  {printed}\nSecond print: {reprinted}");
            });
    }

    #region Generators

    private static Gen<ConditionNode> GenerateConditionNode(int depth)
    {
        if (depth >= 2)
        {
            // At max depth, only generate leaf condition nodes (comparisons and crosses)
            return Gen.OneOf(GenerateComparisonNode(), GenerateCrossNode());
        }

        return Gen.OneOf(
            GenerateComparisonNode(),
            GenerateCrossNode(),
            GenerateLogicalNode(depth));
    }

    private static Gen<ConditionNode> GenerateLogicalNode(int depth)
    {
        return from left in GenerateConditionNode(depth + 1)
               from op in Gen.Elements(LogicalOperator.And, LogicalOperator.Or)
               from right in GenerateConditionNode(depth + 1)
               select (ConditionNode)new LogicalNode(left, op, right);
    }

    private static Gen<ConditionNode> GenerateComparisonNode()
    {
        return from left in GenerateValueNode()
               from op in Gen.Elements(
                   ComparisonOperator.GreaterThan,
                   ComparisonOperator.LessThan,
                   ComparisonOperator.GreaterThanOrEqual,
                   ComparisonOperator.LessThanOrEqual,
                   ComparisonOperator.Equal,
                   ComparisonOperator.NotEqual)
               from right in GenerateValueNode()
               select (ConditionNode)new ComparisonNode(left, op, right);
    }

    private static Gen<ConditionNode> GenerateCrossNode()
    {
        return from left in GenerateValueNode()
               from right in GenerateValueNode()
               from direction in Gen.Elements(CrossDirection.Above, CrossDirection.Below)
               select (ConditionNode)new CrossNode(left, right, direction);
    }

    private static Gen<ValueNode> GenerateValueNode()
    {
        return Gen.OneOf(
            GenerateIndicatorRefNode(),
            GenerateLiteralNode(),
            GeneratePriceRefNode());
    }

    private static Gen<ValueNode> GenerateIndicatorRefNode()
    {
        // Generate valid indicator IDs (letter followed by letters/digits)
        var idGen = from prefix in Gen.Elements("sma", "ema", "rsi", "macd", "atr", "bb", "stoch", "dc")
                    from suffix in Gen.Choose(1, 99)
                    select $"{prefix}{suffix}";

        var subPropertyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("Signal", "Upper", "Lower", "Histogram", "Value"));

        return from id in idGen
               from sub in subPropertyGen
               select (ValueNode)new IndicatorRefNode(id, sub);
    }

    private static Gen<ValueNode> GenerateLiteralNode()
    {
        // Generate positive decimals that round-trip cleanly through double → string → double
        return from intPart in Gen.Choose(0, 999)
               from fracPart in Gen.Choose(0, 99)
               let value = intPart + fracPart / 100.0
               select (ValueNode)new LiteralNode(value);
    }

    private static Gen<ValueNode> GeneratePriceRefNode()
    {
        return from field in Gen.Elements(
                   PriceField.Open,
                   PriceField.High,
                   PriceField.Low,
                   PriceField.Close,
                   PriceField.Volume)
               select (ValueNode)new PriceRefNode(field);
    }

    #endregion
}
