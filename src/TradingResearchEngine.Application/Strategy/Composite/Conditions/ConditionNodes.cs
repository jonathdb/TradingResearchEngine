namespace TradingResearchEngine.Application.Strategy.Composite.Conditions;

/// <summary>
/// Base type for all condition expression AST nodes.
/// Condition nodes represent boolean-producing expressions in the condition language.
/// </summary>
public abstract record ConditionNode;

/// <summary>
/// Logical AND/OR combination of two sub-expressions.
/// Evaluates left-to-right with short-circuit semantics.
/// </summary>
/// <param name="Left">The left operand condition node.</param>
/// <param name="Operator">The logical operator (AND or OR).</param>
/// <param name="Right">The right operand condition node.</param>
public sealed record LogicalNode(
    ConditionNode Left,
    LogicalOperator Operator,
    ConditionNode Right) : ConditionNode;

/// <summary>
/// Comparison between two value expressions using a relational operator.
/// Produces a boolean result from comparing two numeric values.
/// </summary>
/// <param name="Left">The left value expression.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Right">The right value expression.</param>
public sealed record ComparisonNode(
    ValueNode Left,
    ComparisonOperator Operator,
    ValueNode Right) : ConditionNode;

/// <summary>
/// Cross-detection node representing crosses_above(a, b) or crosses_below(a, b).
/// True only on the bar where the crossover occurs based on current and previous values.
/// </summary>
/// <param name="Left">The first value expression (the crossing series).</param>
/// <param name="Right">The second value expression (the crossed series).</param>
/// <param name="Direction">The cross direction (Above or Below).</param>
public sealed record CrossNode(
    ValueNode Left,
    ValueNode Right,
    CrossDirection Direction) : ConditionNode;

/// <summary>
/// Base type for value-producing expressions in the condition language.
/// Value nodes resolve to a nullable double at evaluation time.
/// </summary>
public abstract record ValueNode;

/// <summary>
/// Reference to an indicator value by its unique ID, optionally with a sub-property
/// for multi-value indicators (e.g., "macd1.Signal", "bollinger1.Upper").
/// </summary>
/// <param name="IndicatorId">The unique indicator identifier as defined in the strategy config.</param>
/// <param name="SubProperty">Optional sub-property name for multi-value indicators (e.g., "Signal", "Upper").</param>
public sealed record IndicatorRefNode(string IndicatorId, string? SubProperty = null) : ValueNode;

/// <summary>
/// Reference to a price field from the current bar (open, high, low, close, volume).
/// </summary>
/// <param name="Field">The price field to reference.</param>
public sealed record PriceRefNode(PriceField Field) : ValueNode;

/// <summary>
/// A numeric literal constant used in threshold comparisons (e.g., rsi14 &lt; 30).
/// </summary>
/// <param name="Value">The numeric constant value.</param>
public sealed record LiteralNode(double Value) : ValueNode;

/// <summary>
/// Logical operators for combining boolean sub-expressions.
/// </summary>
public enum LogicalOperator
{
    /// <summary>Logical AND — both operands must be true.</summary>
    And,

    /// <summary>Logical OR — at least one operand must be true.</summary>
    Or
}

/// <summary>
/// Comparison operators for relational expressions between numeric values.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Greater than (&gt;).</summary>
    GreaterThan,

    /// <summary>Less than (&lt;).</summary>
    LessThan,

    /// <summary>Greater than or equal (&gt;=).</summary>
    GreaterThanOrEqual,

    /// <summary>Less than or equal (&lt;=).</summary>
    LessThanOrEqual,

    /// <summary>Equal (==).</summary>
    Equal,

    /// <summary>Not equal (!=).</summary>
    NotEqual
}

/// <summary>
/// Cross direction for the crosses_above and crosses_below functions.
/// </summary>
public enum CrossDirection
{
    /// <summary>Crosses above — left crosses from below to above right.</summary>
    Above,

    /// <summary>Crosses below — left crosses from above to below right.</summary>
    Below
}

/// <summary>
/// Price fields available for reference in condition expressions.
/// </summary>
public enum PriceField
{
    /// <summary>The bar's opening price.</summary>
    Open,

    /// <summary>The bar's highest price.</summary>
    High,

    /// <summary>The bar's lowest price.</summary>
    Low,

    /// <summary>The bar's closing price.</summary>
    Close,

    /// <summary>The bar's volume.</summary>
    Volume
}
