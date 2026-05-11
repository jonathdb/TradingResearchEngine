using System.Linq.Expressions;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Strategies.Composite.Conditions;

/// <summary>
/// Compiles a validated condition AST into a <c>Func&lt;IndicatorValueProvider, BarRecord, bool&gt;</c>
/// delegate using <see cref="System.Linq.Expressions"/> for zero-allocation per-bar evaluation.
/// Short-circuit semantics are preserved for AND/OR via <see cref="Expression.AndAlso"/>
/// and <see cref="Expression.OrElse"/>. Null indicator values are treated as non-triggering (return false).
/// </summary>
public static class ExpressionCompiler
{
    /// <summary>
    /// Compiles an AST node tree into an executable delegate.
    /// The delegate accepts the current <see cref="IndicatorValueProvider"/> and <see cref="BarRecord"/>
    /// and returns a boolean indicating whether the condition is satisfied.
    /// </summary>
    /// <param name="ast">The validated condition AST to compile.</param>
    /// <returns>A compiled delegate for zero-allocation per-bar evaluation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ast"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the AST contains an unsupported node type.</exception>
    public static Func<IndicatorValueProvider, BarRecord, bool> Compile(ConditionNode ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var valueProviderParam = Expression.Parameter(typeof(IndicatorValueProvider), "valueProvider");
        var barParam = Expression.Parameter(typeof(BarRecord), "bar");

        var context = new CompilationContext(valueProviderParam, barParam);
        var body = CompileCondition(ast, context);

        var lambda = Expression.Lambda<Func<IndicatorValueProvider, BarRecord, bool>>(
            body, valueProviderParam, barParam);

        return lambda.Compile();
    }

    private static Expression CompileCondition(ConditionNode node, CompilationContext ctx)
    {
        return node switch
        {
            LogicalNode logical => CompileLogical(logical, ctx),
            ComparisonNode comparison => CompileComparison(comparison, ctx),
            CrossNode cross => CompileCross(cross, ctx),
            _ => throw new InvalidOperationException($"Unsupported condition node type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Compiles a LogicalNode using AndAlso/OrElse for short-circuit semantics.
    /// </summary>
    private static Expression CompileLogical(LogicalNode node, CompilationContext ctx)
    {
        var left = CompileCondition(node.Left, ctx);
        var right = CompileCondition(node.Right, ctx);

        return node.Operator switch
        {
            LogicalOperator.And => Expression.AndAlso(left, right),
            LogicalOperator.Or => Expression.OrElse(left, right),
            _ => throw new InvalidOperationException($"Unsupported logical operator: {node.Operator}")
        };
    }

    /// <summary>
    /// Compiles a ComparisonNode. If either operand is null (indicator not warm), returns false.
    /// Pattern: left.HasValue &amp;&amp; right.HasValue &amp;&amp; (left.Value op right.Value)
    /// </summary>
    private static Expression CompileComparison(ComparisonNode node, CompilationContext ctx)
    {
        var left = CompileValue(node.Left, ctx);
        var right = CompileValue(node.Right, ctx);

        // Both left and right are Expression<decimal?>
        // We need: left.HasValue && right.HasValue && (left.Value op right.Value)
        var leftHasValue = Expression.Property(left, nameof(Nullable<decimal>.HasValue));
        var rightHasValue = Expression.Property(right, nameof(Nullable<decimal>.HasValue));
        var leftValue = Expression.Property(left, nameof(Nullable<decimal>.Value));
        var rightValue = Expression.Property(right, nameof(Nullable<decimal>.Value));

        var comparison = node.Operator switch
        {
            ComparisonOperator.GreaterThan => Expression.GreaterThan(leftValue, rightValue),
            ComparisonOperator.LessThan => Expression.LessThan(leftValue, rightValue),
            ComparisonOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(leftValue, rightValue),
            ComparisonOperator.LessThanOrEqual => Expression.LessThanOrEqual(leftValue, rightValue),
            ComparisonOperator.Equal => Expression.Equal(leftValue, rightValue),
            ComparisonOperator.NotEqual => Expression.NotEqual(leftValue, rightValue),
            _ => throw new InvalidOperationException($"Unsupported comparison operator: {node.Operator}")
        };

        // Short-circuit: if left is null, don't evaluate right
        return Expression.AndAlso(
            Expression.AndAlso(leftHasValue, rightHasValue),
            comparison);
    }

    /// <summary>
    /// Compiles a CrossNode.
    /// CrossAbove: leftCurrent > rightCurrent AND leftPrevious &lt;= rightPrevious
    /// CrossBelow: leftCurrent &lt; rightCurrent AND leftPrevious >= rightPrevious
    /// All four values must be non-null; if any is null, returns false.
    /// </summary>
    private static Expression CompileCross(CrossNode node, CompilationContext ctx)
    {
        var leftCurrent = CompileValue(node.Left, ctx);
        var rightCurrent = CompileValue(node.Right, ctx);
        var leftPrevious = CompilePreviousValue(node.Left, ctx);
        var rightPrevious = CompilePreviousValue(node.Right, ctx);

        // All four must have values
        var leftCurrentHasValue = Expression.Property(leftCurrent, nameof(Nullable<decimal>.HasValue));
        var rightCurrentHasValue = Expression.Property(rightCurrent, nameof(Nullable<decimal>.HasValue));
        var leftPreviousHasValue = Expression.Property(leftPrevious, nameof(Nullable<decimal>.HasValue));
        var rightPreviousHasValue = Expression.Property(rightPrevious, nameof(Nullable<decimal>.HasValue));

        var allHaveValues = Expression.AndAlso(
            Expression.AndAlso(leftCurrentHasValue, rightCurrentHasValue),
            Expression.AndAlso(leftPreviousHasValue, rightPreviousHasValue));

        // Extract .Value from each
        var leftCurrentVal = Expression.Property(leftCurrent, nameof(Nullable<decimal>.Value));
        var rightCurrentVal = Expression.Property(rightCurrent, nameof(Nullable<decimal>.Value));
        var leftPreviousVal = Expression.Property(leftPrevious, nameof(Nullable<decimal>.Value));
        var rightPreviousVal = Expression.Property(rightPrevious, nameof(Nullable<decimal>.Value));

        Expression crossCondition = node.Direction switch
        {
            // CrossAbove: left_current > right_current AND left_previous <= right_previous
            CrossDirection.Above => Expression.AndAlso(
                Expression.GreaterThan(leftCurrentVal, rightCurrentVal),
                Expression.LessThanOrEqual(leftPreviousVal, rightPreviousVal)),

            // CrossBelow: left_current < right_current AND left_previous >= right_previous
            CrossDirection.Below => Expression.AndAlso(
                Expression.LessThan(leftCurrentVal, rightCurrentVal),
                Expression.GreaterThanOrEqual(leftPreviousVal, rightPreviousVal)),

            _ => throw new InvalidOperationException($"Unsupported cross direction: {node.Direction}")
        };

        return Expression.AndAlso(allHaveValues, crossCondition);
    }

    /// <summary>
    /// Compiles a ValueNode into an expression that produces a <c>decimal?</c>.
    /// </summary>
    private static Expression CompileValue(ValueNode node, CompilationContext ctx)
    {
        return node switch
        {
            IndicatorRefNode indicatorRef => CompileIndicatorRef(indicatorRef, ctx),
            PriceRefNode priceRef => CompilePriceRef(priceRef, ctx),
            LiteralNode literal => CompileLiteral(literal),
            _ => throw new InvalidOperationException($"Unsupported value node type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Compiles a ValueNode into an expression that produces the previous <c>decimal?</c> value.
    /// Used for cross-detection.
    /// </summary>
    private static Expression CompilePreviousValue(ValueNode node, CompilationContext ctx)
    {
        return node switch
        {
            IndicatorRefNode indicatorRef => CompileIndicatorPreviousRef(indicatorRef, ctx),
            PriceRefNode => CompileNullDecimal(), // Price has no "previous" in single-bar context
            LiteralNode literal => CompileLiteral(literal), // Literals don't change
            _ => throw new InvalidOperationException($"Unsupported value node type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Compiles an IndicatorRefNode: calls valueProvider.GetValue(reference).
    /// </summary>
    private static Expression CompileIndicatorRef(IndicatorRefNode node, CompilationContext ctx)
    {
        var reference = node.SubProperty is null
            ? node.IndicatorId
            : $"{node.IndicatorId}.{node.SubProperty}";

        var getValueMethod = typeof(IndicatorValueProvider).GetMethod(
            nameof(IndicatorValueProvider.GetValue),
            [typeof(string)])!;

        return Expression.Call(
            ctx.ValueProvider,
            getValueMethod,
            Expression.Constant(reference));
    }

    /// <summary>
    /// Compiles an IndicatorRefNode for previous value: calls valueProvider.GetPreviousValue(reference).
    /// </summary>
    private static Expression CompileIndicatorPreviousRef(IndicatorRefNode node, CompilationContext ctx)
    {
        var reference = node.SubProperty is null
            ? node.IndicatorId
            : $"{node.IndicatorId}.{node.SubProperty}";

        var getPreviousValueMethod = typeof(IndicatorValueProvider).GetMethod(
            nameof(IndicatorValueProvider.GetPreviousValue),
            [typeof(string)])!;

        return Expression.Call(
            ctx.ValueProvider,
            getPreviousValueMethod,
            Expression.Constant(reference));
    }

    /// <summary>
    /// Compiles a PriceRefNode: accesses the appropriate property on BarRecord as decimal?.
    /// </summary>
    private static Expression CompilePriceRef(PriceRefNode node, CompilationContext ctx)
    {
        var propertyName = node.Field switch
        {
            PriceField.Open => nameof(BarRecord.Open),
            PriceField.High => nameof(BarRecord.High),
            PriceField.Low => nameof(BarRecord.Low),
            PriceField.Close => nameof(BarRecord.Close),
            PriceField.Volume => nameof(BarRecord.Volume),
            _ => throw new InvalidOperationException($"Unsupported price field: {node.Field}")
        };

        // BarRecord properties are decimal (non-nullable), wrap in decimal?
        var property = Expression.Property(ctx.Bar, propertyName);
        return Expression.Convert(property, typeof(decimal?));
    }

    /// <summary>
    /// Compiles a LiteralNode: constant decimal? value.
    /// </summary>
    private static Expression CompileLiteral(LiteralNode node)
    {
        var value = (decimal)node.Value;
        return Expression.Constant((decimal?)value, typeof(decimal?));
    }

    /// <summary>
    /// Returns a constant null decimal? expression (used for price previous values in cross context).
    /// </summary>
    private static Expression CompileNullDecimal()
    {
        return Expression.Constant(null, typeof(decimal?));
    }

    /// <summary>
    /// Holds the parameter expressions used throughout compilation.
    /// </summary>
    private sealed class CompilationContext
    {
        public ParameterExpression ValueProvider { get; }
        public ParameterExpression Bar { get; }

        public CompilationContext(ParameterExpression valueProvider, ParameterExpression bar)
        {
            ValueProvider = valueProvider;
            Bar = bar;
        }
    }
}
