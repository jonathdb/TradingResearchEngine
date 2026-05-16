using TradingResearchEngine.Application.Strategies.Composite.Conditions;
using ExpressionCompiler = TradingResearchEngine.Application.Strategies.Composite.Conditions.ExpressionCompiler;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Comprehensive negative test coverage for <see cref="ExpressionCompiler"/>.
/// Verifies that all malformed inputs produce descriptive <see cref="ExpressionCompileError"/>
/// rather than unhandled exceptions or valid compiled results.
/// Requirements: 34.1, 34.2, 34.3
/// </summary>
public class ExpressionCompilerNegativeTests
{
    private static readonly IReadOnlyList<string> DefinedIndicators = new[] { "rsi14", "sma20", "ema10", "macd1" };

    #region Empty Expressions

    [Fact]
    public void TryCompileExpression_NullExpression_ReturnsEmptyExpressionError()
    {
        var success = ExpressionCompiler.TryCompileExpression(null, DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.EmptyExpression, error.Kind);
        Assert.Contains("null", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCompileExpression_EmptyString_ReturnsEmptyExpressionError()
    {
        var success = ExpressionCompiler.TryCompileExpression("", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.EmptyExpression, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_WhitespaceOnly_ReturnsEmptyExpressionError()
    {
        var success = ExpressionCompiler.TryCompileExpression("   \t\n  ", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.EmptyExpression, error.Kind);
    }

    #endregion

    #region Missing Operators

    [Fact]
    public void TryCompileExpression_MissingOperatorBetweenIdentifiers_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public void TryCompileExpression_MissingLogicalOperatorBetweenComparisons_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > 70 sma20 > 50", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_MissingComparisonOperator_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 AND sma20 > 50", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_TrailingOperator_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 >", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_LeadingOperator_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("> 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_DoubleOperator_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > > 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_TrailingAndKeyword_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > 70 AND", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_TrailingOrKeyword_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > 70 OR", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    #endregion

    #region Unbalanced Parentheses

    [Fact]
    public void TryCompileExpression_UnclosedLeftParen_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("(rsi14 > 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_ExtraRightParen_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > 70)", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_MultipleUnclosedParens_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("((rsi14 > 70)", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_EmptyParens_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("()", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_MismatchedParensInCrossFunction_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("crosses_above(rsi14, sma20", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_OnlyLeftParen_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("(", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_OnlyRightParen_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression(")", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    #endregion

    #region Invalid Identifiers

    [Fact]
    public void TryCompileExpression_UndefinedIndicator_ReturnsInvalidIdentifierError()
    {
        var success = ExpressionCompiler.TryCompileExpression("unknown_indicator > 50", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.InvalidIdentifier, error.Kind);
        Assert.Contains("unknown_indicator", error.Message);
    }

    [Fact]
    public void TryCompileExpression_MultipleUndefinedIndicators_ReturnsInvalidIdentifierError()
    {
        var success = ExpressionCompiler.TryCompileExpression("foo > 50 AND bar < 30", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.InvalidIdentifier, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_IdentifierStartingWithSpecialChar_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("@invalid > 50", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_IdentifierStartingWithDigit_ReturnsSyntaxError()
    {
        // "123abc > 50" — the parser will read "123" as a number, then "abc" as an identifier
        // This results in "123 abc > 50" which is a missing operator between number and identifier
        var success = ExpressionCompiler.TryCompileExpression("123abc > 50", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        // This will be a syntax error because the parser reads 123 as a number then expects an operator
        Assert.True(error.Kind is ExpressionErrorKind.SyntaxError or ExpressionErrorKind.InvalidIdentifier);
    }

    [Fact]
    public void TryCompileExpression_DotNotationWithUndefinedBase_ReturnsInvalidIdentifierError()
    {
        var success = ExpressionCompiler.TryCompileExpression("undefined.Signal > 0", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.InvalidIdentifier, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_SpecialCharactersInExpression_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 # 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_DollarSignInExpression_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("$rsi14 > 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    #endregion

    #region Deeply Nested Expressions

    [Fact]
    public void TryCompileExpression_DeeplyNestedParentheses_ReturnsExcessiveNestingError()
    {
        // Create an expression with nesting depth > 50 (the MaxParseDepth)
        var depth = 55;
        var expression = new string('(', depth) + "rsi14 > 70" + new string(')', depth);

        var success = ExpressionCompiler.TryCompileExpression(expression, DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.ExcessiveNesting, error.Kind);
        Assert.Contains("depth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCompileExpression_DeeplyNestedLogicalExpressions_ReturnsExcessiveNestingError()
    {
        // Build deeply nested AND chain with parentheses forcing recursion
        // Each "(x > 1 AND " adds a level of nesting
        var parts = new List<string>();
        for (int i = 0; i < 55; i++)
        {
            parts.Add("(rsi14 > 70 AND ");
        }
        parts.Add("rsi14 > 70");
        for (int i = 0; i < 55; i++)
        {
            parts.Add(")");
        }
        var expression = string.Concat(parts);

        var success = ExpressionCompiler.TryCompileExpression(expression, DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.ExcessiveNesting, error.Kind);
    }

    #endregion

    #region Malformed Cross Functions

    [Fact]
    public void TryCompileExpression_CrossFunctionMissingArguments_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("crosses_above()", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_CrossFunctionMissingSecondArgument_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("crosses_above(rsi14)", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_CrossFunctionMissingParens_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("crosses_above rsi14, sma20", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_CrossFunctionMissingComma_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("crosses_above(rsi14 sma20)", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    #endregion

    #region Miscellaneous Malformed Inputs

    [Fact]
    public void TryCompileExpression_OnlyOperator_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression(">", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_OnlyKeyword_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("AND", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_IncompleteEqualsOperator_ReturnsSyntaxError()
    {
        // Single '=' is not a valid operator (must be '==')
        var success = ExpressionCompiler.TryCompileExpression("rsi14 = 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_IncompleteNotEqualsOperator_ReturnsSyntaxError()
    {
        // Single '!' without '=' is not valid
        var success = ExpressionCompiler.TryCompileExpression("rsi14 ! 70", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_RandomPunctuation_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression(";;;", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.Equal(ExpressionErrorKind.SyntaxError, error.Kind);
    }

    [Fact]
    public void TryCompileExpression_SqlInjectionAttempt_ReturnsSyntaxError()
    {
        var success = ExpressionCompiler.TryCompileExpression("1; DROP TABLE users;--", DefinedIndicators, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.NotNull(error);
        Assert.True(error.Kind is ExpressionErrorKind.SyntaxError or ExpressionErrorKind.InvalidIdentifier);
    }

    #endregion

    #region Valid Expressions Compile Successfully

    [Fact]
    public void TryCompileExpression_ValidSimpleComparison_Succeeds()
    {
        var success = ExpressionCompiler.TryCompileExpression("rsi14 > 70", DefinedIndicators, out var compiled, out var error);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.Null(error);
    }

    [Fact]
    public void TryCompileExpression_ValidComplexExpression_Succeeds()
    {
        var success = ExpressionCompiler.TryCompileExpression(
            "rsi14 > 70 AND sma20 > close OR ema10 < 50",
            DefinedIndicators,
            out var compiled,
            out var error);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.Null(error);
    }

    [Fact]
    public void TryCompileExpression_ValidCrossFunction_Succeeds()
    {
        var success = ExpressionCompiler.TryCompileExpression(
            "crosses_above(sma20, ema10)",
            DefinedIndicators,
            out var compiled,
            out var error);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.Null(error);
    }

    [Fact]
    public void TryCompileExpression_ValidWithNullIndicatorIds_SkipsValidation()
    {
        // When definedIndicatorIds is null, validation is skipped
        var success = ExpressionCompiler.TryCompileExpression(
            "any_indicator > 50",
            null,
            out var compiled,
            out var error);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.Null(error);
    }

    #endregion

    #region Error Descriptiveness

    [Fact]
    public void TryCompileExpression_ErrorContainsOriginalExpression()
    {
        const string badExpression = "rsi14 > > 70";
        ExpressionCompiler.TryCompileExpression(badExpression, DefinedIndicators, out _, out var error);

        Assert.NotNull(error);
        Assert.Equal(badExpression, error.Expression);
    }

    [Fact]
    public void TryCompileExpression_ErrorContainsInnerException()
    {
        ExpressionCompiler.TryCompileExpression("(rsi14 > 70", DefinedIndicators, out _, out var error);

        Assert.NotNull(error);
        Assert.NotNull(error.InnerException);
        Assert.IsType<ConditionParseException>(error.InnerException);
    }

    [Fact]
    public void TryCompileExpression_ErrorMessageIsDescriptive()
    {
        ExpressionCompiler.TryCompileExpression("", DefinedIndicators, out _, out var error);

        Assert.NotNull(error);
        Assert.NotEmpty(error.Message);
        Assert.True(error.Message.Length > 10, "Error message should be descriptive, not just a code");
    }

    #endregion
}
