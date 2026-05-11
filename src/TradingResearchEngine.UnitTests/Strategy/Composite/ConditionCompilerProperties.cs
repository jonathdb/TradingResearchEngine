using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Moq;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Application.Strategies.Composite.Conditions;
using TradingResearchEngine.Core.DataHandling;
using ExpressionCompiler = TradingResearchEngine.Application.Strategies.Composite.Conditions.ExpressionCompiler;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for the ExpressionCompiler determinism.
/// **Validates: Requirements 6.1, 6.2**
/// </summary>
public class ConditionCompilerProperties
{
    // Feature: composite-strategy-engine, Property 3: Compiled expression determinism
    /// <summary>
    /// For any valid condition expression and any indicator value state,
    /// evaluating the compiled delegate SHALL produce a deterministic boolean result
    /// identical to interpreting the AST directly.
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CompiledExpression_MatchesDirectAstInterpretation()
    {
        var gen = from ast in GenerateConditionNode(depth: 0)
                  from values in GenerateIndicatorValues()
                  select (ast, values);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (ast, values) = t;

                // Set up the IndicatorValueProvider with mock instances
                var provider = CreateProviderWithValues(values);
                var bar = CreateTestBar();

                // Compile and evaluate
                var compiled = ExpressionCompiler.Compile(ast);
                var compiledResult = compiled(provider, bar);

                // Interpret directly
                var interpretedResult = InterpretAst(ast, provider, bar);

                return (compiledResult == interpretedResult)
                    .Label($"Compiled={compiledResult}, Interpreted={interpretedResult}, AST={ConditionPrettyPrinter.Print(ast)}");
            });
    }

    #region Direct AST Interpreter

    private static bool InterpretAst(ConditionNode node, IndicatorValueProvider provider, BarRecord bar)
    {
        return node switch
        {
            LogicalNode logical => InterpretLogical(logical, provider, bar),
            ComparisonNode comparison => InterpretComparison(comparison, provider, bar),
            CrossNode cross => InterpretCross(cross, provider, bar),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }

    private static bool InterpretLogical(LogicalNode node, IndicatorValueProvider provider, BarRecord bar)
    {
        var left = InterpretAst(node.Left, provider, bar);
        return node.Operator switch
        {
            LogicalOperator.And => left && InterpretAst(node.Right, provider, bar),
            LogicalOperator.Or => left || InterpretAst(node.Right, provider, bar),
            _ => throw new InvalidOperationException()
        };
    }

    private static bool InterpretComparison(ComparisonNode node, IndicatorValueProvider provider, BarRecord bar)
    {
        var left = ResolveValue(node.Left, provider, bar);
        var right = ResolveValue(node.Right, provider, bar);

        if (!left.HasValue || !right.HasValue)
            return false;

        return node.Operator switch
        {
            ComparisonOperator.GreaterThan => left.Value > right.Value,
            ComparisonOperator.LessThan => left.Value < right.Value,
            ComparisonOperator.GreaterThanOrEqual => left.Value >= right.Value,
            ComparisonOperator.LessThanOrEqual => left.Value <= right.Value,
            ComparisonOperator.Equal => left.Value == right.Value,
            ComparisonOperator.NotEqual => left.Value != right.Value,
            _ => throw new InvalidOperationException()
        };
    }

    private static bool InterpretCross(CrossNode node, IndicatorValueProvider provider, BarRecord bar)
    {
        var leftCurrent = ResolveValue(node.Left, provider, bar);
        var rightCurrent = ResolveValue(node.Right, provider, bar);
        var leftPrevious = ResolvePreviousValue(node.Left, provider);
        var rightPrevious = ResolvePreviousValue(node.Right, provider);

        if (!leftCurrent.HasValue || !rightCurrent.HasValue ||
            !leftPrevious.HasValue || !rightPrevious.HasValue)
            return false;

        return node.Direction switch
        {
            CrossDirection.Above =>
                leftCurrent.Value > rightCurrent.Value && leftPrevious.Value <= rightPrevious.Value,
            CrossDirection.Below =>
                leftCurrent.Value < rightCurrent.Value && leftPrevious.Value >= rightPrevious.Value,
            _ => throw new InvalidOperationException()
        };
    }

    private static decimal? ResolveValue(ValueNode node, IndicatorValueProvider provider, BarRecord bar)
    {
        return node switch
        {
            IndicatorRefNode ind => provider.GetValue(
                ind.SubProperty is null ? ind.IndicatorId : $"{ind.IndicatorId}.{ind.SubProperty}"),
            PriceRefNode price => price.Field switch
            {
                PriceField.Open => bar.Open,
                PriceField.High => bar.High,
                PriceField.Low => bar.Low,
                PriceField.Close => bar.Close,
                PriceField.Volume => bar.Volume,
                _ => null
            },
            LiteralNode literal => (decimal)literal.Value,
            _ => null
        };
    }

    private static decimal? ResolvePreviousValue(ValueNode node, IndicatorValueProvider provider)
    {
        return node switch
        {
            IndicatorRefNode ind => provider.GetPreviousValue(
                ind.SubProperty is null ? ind.IndicatorId : $"{ind.IndicatorId}.{ind.SubProperty}"),
            PriceRefNode => null, // Price has no "previous" in single-bar context
            LiteralNode literal => (decimal)literal.Value,
            _ => null
        };
    }

    #endregion

    #region Test Helpers

    private static IndicatorValueProvider CreateProviderWithValues(Dictionary<string, (decimal? Current, decimal? Previous)> values)
    {
        var provider = new IndicatorValueProvider();
        var instances = new List<IIndicatorInstance>();

        // Group by base indicator ID
        var grouped = new Dictionary<string, MockIndicatorData>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, val) in values)
        {
            var dotIndex = key.IndexOf('.');
            var baseId = dotIndex >= 0 ? key[..dotIndex] : key;
            var subProp = dotIndex >= 0 ? key[(dotIndex + 1)..] : null;

            if (!grouped.TryGetValue(baseId, out var data))
            {
                data = new MockIndicatorData(baseId);
                grouped[baseId] = data;
            }

            if (subProp is null)
            {
                data.CurrentValue = val.Current;
                data.PreviousValue = val.Previous;
            }
            else
            {
                data.SubValues[subProp] = val.Current;
                data.PreviousSubValues[subProp] = val.Previous;
            }
        }

        foreach (var (id, data) in grouped)
        {
            var mock = new Mock<IIndicatorInstance>();
            mock.Setup(m => m.Id).Returns(id);
            mock.Setup(m => m.Type).Returns("mock");
            mock.Setup(m => m.IsWarm).Returns(true);
            mock.Setup(m => m.CurrentValue).Returns(data.CurrentValue);
            mock.Setup(m => m.PreviousValue).Returns(data.PreviousValue);
            mock.Setup(m => m.GetSubValue(It.IsAny<string>()))
                .Returns<string>(sub => data.SubValues.GetValueOrDefault(sub));
            mock.Setup(m => m.GetPreviousSubValue(It.IsAny<string>()))
                .Returns<string>(sub => data.PreviousSubValues.GetValueOrDefault(sub));
            instances.Add(mock.Object);
        }

        provider.Update(instances);
        return provider;
    }

    private static BarRecord CreateTestBar()
    {
        return new BarRecord(
            Symbol: "TEST",
            Interval: "D1",
            Open: 100m,
            High: 105m,
            Low: 95m,
            Close: 102m,
            Volume: 1000m,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private sealed class MockIndicatorData
    {
        public string Id { get; }
        public decimal? CurrentValue { get; set; }
        public decimal? PreviousValue { get; set; }
        public Dictionary<string, decimal?> SubValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal?> PreviousSubValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MockIndicatorData(string id) => Id = id;
    }

    #endregion

    #region Generators

    private static Gen<ConditionNode> GenerateConditionNode(int depth)
    {
        if (depth >= 2)
        {
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
        // For crosses, only use indicator refs (not price/literal) to ensure previous values exist
        return from left in GenerateIndicatorRefNode()
               from right in GenerateIndicatorRefNode()
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
        var idGen = from prefix in Gen.Elements("sma", "ema", "rsi", "macd", "atr")
                    from suffix in Gen.Choose(1, 50)
                    select $"{prefix}{suffix}";

        var subPropertyGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("Signal", "Upper", "Lower"));

        return from id in idGen
               from sub in subPropertyGen
               select (ValueNode)new IndicatorRefNode(id, sub);
    }

    private static Gen<ValueNode> GenerateLiteralNode()
    {
        return from intPart in Gen.Choose(0, 200)
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

    /// <summary>
    /// Generates a dictionary of indicator values that covers all indicators
    /// that might be referenced by the generated AST.
    /// </summary>
    private static Gen<Dictionary<string, (decimal? Current, decimal? Previous)>> GenerateIndicatorValues()
    {
        // Generate values for all possible indicator IDs that our generators can produce
        var prefixes = new[] { "sma", "ema", "rsi", "macd", "atr" };
        var subProps = new[] { "Signal", "Upper", "Lower" };

        return from seed in Gen.Choose(1, 10000)
               let rng = new Random(seed)
               select BuildValueDictionary(rng, prefixes, subProps);
    }

    private static Dictionary<string, (decimal? Current, decimal? Previous)> BuildValueDictionary(
        Random rng, string[] prefixes, string[] subProps)
    {
        var dict = new Dictionary<string, (decimal? Current, decimal? Previous)>(StringComparer.OrdinalIgnoreCase);

        foreach (var prefix in prefixes)
        {
            for (var i = 1; i <= 50; i++)
            {
                var id = $"{prefix}{i}";
                var current = (decimal)(rng.NextDouble() * 200);
                var previous = (decimal)(rng.NextDouble() * 200);
                dict[id] = (current, previous);

                foreach (var sub in subProps)
                {
                    var subCurrent = (decimal)(rng.NextDouble() * 200);
                    var subPrevious = (decimal)(rng.NextDouble() * 200);
                    dict[$"{id}.{sub}"] = (subCurrent, subPrevious);
                }
            }
        }

        return dict;
    }

    #endregion
}
