using TradingResearchEngine.Application.Strategies.Composite.Conditions;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Unit tests for the ConditionParser covering all grammar productions,
/// operator types, error handling, and validation.
/// </summary>
public class ConditionParserTests
{
    #region Comparison Operators

    [Theory]
    [InlineData("rsi14 > 70", ComparisonOperator.GreaterThan)]
    [InlineData("rsi14 < 30", ComparisonOperator.LessThan)]
    [InlineData("rsi14 >= 50", ComparisonOperator.GreaterThanOrEqual)]
    [InlineData("rsi14 <= 50", ComparisonOperator.LessThanOrEqual)]
    [InlineData("rsi14 == 50", ComparisonOperator.Equal)]
    [InlineData("rsi14 != 50", ComparisonOperator.NotEqual)]
    public void Parse_ComparisonOperator_ProducesCorrectAstNode(string expression, ComparisonOperator expectedOp)
    {
        var ast = ConditionParser.Parse(expression);

        var comparison = Assert.IsType<ComparisonNode>(ast);
        Assert.Equal(expectedOp, comparison.Operator);
        var left = Assert.IsType<IndicatorRefNode>(comparison.Left);
        Assert.Equal("rsi14", left.IndicatorId);
        var right = Assert.IsType<LiteralNode>(comparison.Right);
        Assert.True(right.Value is >= 30 and <= 70);
    }

    #endregion

    #region Logical Operators

    [Fact]
    public void Parse_AndOperator_CombinesSubExpressionsCorrectly()
    {
        var ast = ConditionParser.Parse("rsi14 > 70 AND sma20 > close");

        var logical = Assert.IsType<LogicalNode>(ast);
        Assert.Equal(LogicalOperator.And, logical.Operator);
        Assert.IsType<ComparisonNode>(logical.Left);
        Assert.IsType<ComparisonNode>(logical.Right);
    }

    [Fact]
    public void Parse_OrOperator_CombinesSubExpressionsCorrectly()
    {
        var ast = ConditionParser.Parse("rsi14 > 70 OR rsi14 < 30");

        var logical = Assert.IsType<LogicalNode>(ast);
        Assert.Equal(LogicalOperator.Or, logical.Operator);
        Assert.IsType<ComparisonNode>(logical.Left);
        Assert.IsType<ComparisonNode>(logical.Right);
    }

    [Fact]
    public void Parse_AndHasHigherPrecedenceThanOr_ProducesCorrectTree()
    {
        // "a > 1 OR b > 2 AND c > 3" should parse as "a > 1 OR (b > 2 AND c > 3)"
        var ast = ConditionParser.Parse("rsi14 > 70 OR sma20 > 50 AND ema10 > 50");

        var orNode = Assert.IsType<LogicalNode>(ast);
        Assert.Equal(LogicalOperator.Or, orNode.Operator);
        Assert.IsType<ComparisonNode>(orNode.Left);
        var andNode = Assert.IsType<LogicalNode>(orNode.Right);
        Assert.Equal(LogicalOperator.And, andNode.Operator);
    }

    #endregion

    #region Parenthesised Grouping

    [Fact]
    public void Parse_ParenthesisedGrouping_OverridesDefaultPrecedence()
    {
        // "(a > 1 OR b > 2) AND c > 3" should parse as "(a > 1 OR b > 2) AND c > 3"
        var ast = ConditionParser.Parse("(rsi14 > 70 OR sma20 > 50) AND ema10 > 50");

        var andNode = Assert.IsType<LogicalNode>(ast);
        Assert.Equal(LogicalOperator.And, andNode.Operator);
        var orNode = Assert.IsType<LogicalNode>(andNode.Left);
        Assert.Equal(LogicalOperator.Or, orNode.Operator);
        Assert.IsType<ComparisonNode>(andNode.Right);
    }

    [Fact]
    public void Parse_NestedParentheses_ParsesCorrectly()
    {
        var ast = ConditionParser.Parse("((rsi14 > 70))");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        Assert.Equal(ComparisonOperator.GreaterThan, comparison.Operator);
    }

    #endregion

    #region Cross Functions

    [Fact]
    public void Parse_CrossesAbove_ProducesCorrectCrossNode()
    {
        var ast = ConditionParser.Parse("crosses_above(sma20, sma50)");

        var cross = Assert.IsType<CrossNode>(ast);
        Assert.Equal(CrossDirection.Above, cross.Direction);
        var left = Assert.IsType<IndicatorRefNode>(cross.Left);
        Assert.Equal("sma20", left.IndicatorId);
        var right = Assert.IsType<IndicatorRefNode>(cross.Right);
        Assert.Equal("sma50", right.IndicatorId);
    }

    [Fact]
    public void Parse_CrossesBelow_ProducesCorrectCrossNode()
    {
        var ast = ConditionParser.Parse("crosses_below(ema10, ema20)");

        var cross = Assert.IsType<CrossNode>(ast);
        Assert.Equal(CrossDirection.Below, cross.Direction);
        var left = Assert.IsType<IndicatorRefNode>(cross.Left);
        Assert.Equal("ema10", left.IndicatorId);
        var right = Assert.IsType<IndicatorRefNode>(cross.Right);
        Assert.Equal("ema20", right.IndicatorId);
    }

    [Fact]
    public void Parse_CrossesAboveWithLiteral_ProducesCorrectCrossNode()
    {
        var ast = ConditionParser.Parse("crosses_above(rsi14, 70)");

        var cross = Assert.IsType<CrossNode>(ast);
        Assert.Equal(CrossDirection.Above, cross.Direction);
        Assert.IsType<IndicatorRefNode>(cross.Left);
        var right = Assert.IsType<LiteralNode>(cross.Right);
        Assert.Equal(70.0, right.Value);
    }

    #endregion

    #region Dot-Notation Indicator References

    [Fact]
    public void Parse_DotNotationIndicatorRef_ProducesCorrectNode()
    {
        var ast = ConditionParser.Parse("macd1.Signal > 0");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var left = Assert.IsType<IndicatorRefNode>(comparison.Left);
        Assert.Equal("macd1", left.IndicatorId);
        Assert.Equal("Signal", left.SubProperty);
    }

    [Fact]
    public void Parse_DotNotationInCrossFunction_ProducesCorrectNode()
    {
        var ast = ConditionParser.Parse("crosses_above(macd1.Signal, macd1.Histogram)");

        var cross = Assert.IsType<CrossNode>(ast);
        var left = Assert.IsType<IndicatorRefNode>(cross.Left);
        Assert.Equal("macd1", left.IndicatorId);
        Assert.Equal("Signal", left.SubProperty);
        var right = Assert.IsType<IndicatorRefNode>(cross.Right);
        Assert.Equal("macd1", right.IndicatorId);
        Assert.Equal("Histogram", right.SubProperty);
    }

    #endregion

    #region Numeric Literals

    [Fact]
    public void Parse_PositiveInteger_ProducesCorrectLiteralNode()
    {
        var ast = ConditionParser.Parse("rsi14 > 70");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var right = Assert.IsType<LiteralNode>(comparison.Right);
        Assert.Equal(70.0, right.Value);
    }

    [Fact]
    public void Parse_NegativeNumber_ProducesCorrectLiteralNode()
    {
        var ast = ConditionParser.Parse("macd1 > -0.5");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var right = Assert.IsType<LiteralNode>(comparison.Right);
        Assert.Equal(-0.5, right.Value);
    }

    [Fact]
    public void Parse_DecimalNumber_ProducesCorrectLiteralNode()
    {
        var ast = ConditionParser.Parse("atr14 > 1.25");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var right = Assert.IsType<LiteralNode>(comparison.Right);
        Assert.Equal(1.25, right.Value);
    }

    [Fact]
    public void Parse_ZeroLiteral_ProducesCorrectLiteralNode()
    {
        var ast = ConditionParser.Parse("macd1 > 0");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var right = Assert.IsType<LiteralNode>(comparison.Right);
        Assert.Equal(0.0, right.Value);
    }

    #endregion

    #region Price References

    [Theory]
    [InlineData("open", PriceField.Open)]
    [InlineData("high", PriceField.High)]
    [InlineData("low", PriceField.Low)]
    [InlineData("close", PriceField.Close)]
    [InlineData("volume", PriceField.Volume)]
    public void Parse_PriceReference_ProducesCorrectPriceRefNode(string priceField, PriceField expectedField)
    {
        var ast = ConditionParser.Parse($"sma20 > {priceField}");

        var comparison = Assert.IsType<ComparisonNode>(ast);
        var right = Assert.IsType<PriceRefNode>(comparison.Right);
        Assert.Equal(expectedField, right.Field);
    }

    #endregion

    #region Invalid Expressions

    [Fact]
    public void Parse_EmptyExpression_ThrowsConditionParseException()
    {
        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse(""));
        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_MissingRightOperand_ThrowsConditionParseException()
    {
        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse("rsi14 >"));
        Assert.True(ex.Position >= 0);
        Assert.NotEmpty(ex.Expected);
        Assert.NotEmpty(ex.Found);
    }

    [Fact]
    public void Parse_InvalidOperator_ThrowsConditionParseException()
    {
        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse("rsi14 & 70"));
        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_UnclosedParenthesis_ThrowsConditionParseException()
    {
        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse("(rsi14 > 70"));
        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_MissingCommaInCrossFunction_ThrowsConditionParseException()
    {
        var ex = Assert.Throws<ConditionParseException>(() => ConditionParser.Parse("crosses_above(sma20 sma50)"));
        Assert.True(ex.Position >= 0);
    }

    #endregion

    #region Validation — Unknown Indicator References

    [Fact]
    public void Validate_UnknownIndicatorReference_ThrowsConditionValidationException()
    {
        var ast = ConditionParser.Parse("unknown_indicator > 50");
        var definedIds = new List<string> { "rsi14", "sma20" };

        var ex = Assert.Throws<ConditionValidationException>(
            () => ConditionValidator.Validate(ast, definedIds));

        Assert.Contains("unknown_indicator", ex.UndefinedReferences);
        Assert.Equal(definedIds, ex.DefinedIndicatorIds);
    }

    [Fact]
    public void Validate_MultipleUnknownReferences_ReportsAll()
    {
        var ast = ConditionParser.Parse("foo > 50 AND bar < 30");
        var definedIds = new List<string> { "rsi14" };

        var ex = Assert.Throws<ConditionValidationException>(
            () => ConditionValidator.Validate(ast, definedIds));

        Assert.Contains("foo", ex.UndefinedReferences);
        Assert.Contains("bar", ex.UndefinedReferences);
    }

    [Fact]
    public void Validate_AllReferencesKnown_DoesNotThrow()
    {
        var ast = ConditionParser.Parse("rsi14 > 70 AND sma20 > close");
        var definedIds = new List<string> { "rsi14", "sma20" };

        // Should not throw
        ConditionValidator.Validate(ast, definedIds);
    }

    [Fact]
    public void Validate_DotNotationReference_ValidatesBaseId()
    {
        var ast = ConditionParser.Parse("macd1.Signal > 0");
        var definedIds = new List<string> { "macd1" };

        // Should not throw — validates the base indicator ID
        ConditionValidator.Validate(ast, definedIds);
    }

    #endregion
}
