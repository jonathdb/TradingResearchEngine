namespace TradingResearchEngine.Application.Strategies.Composite.Conditions;

/// <summary>
/// Thrown when the expression compiler encounters a malformed or invalid expression
/// during the parse, validate, or compile phases. Wraps the underlying cause with
/// a descriptive message suitable for user-facing error reporting.
/// </summary>
public sealed class ExpressionCompileError : Exception
{
    /// <summary>Gets the category of the compilation failure.</summary>
    public ExpressionErrorKind Kind { get; }

    /// <summary>Gets the original expression that failed to compile, if available.</summary>
    public string? Expression { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="ExpressionCompileError"/>.
    /// </summary>
    /// <param name="kind">The category of the compilation failure.</param>
    /// <param name="message">A descriptive error message.</param>
    /// <param name="expression">The original expression that failed, if available.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public ExpressionCompileError(ExpressionErrorKind kind, string message, string? expression = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Expression = expression;
    }
}

/// <summary>
/// Categorises the type of expression compilation failure.
/// </summary>
public enum ExpressionErrorKind
{
    /// <summary>The expression string is null or empty.</summary>
    EmptyExpression,

    /// <summary>The expression contains a syntax error (missing operators, unbalanced parentheses, etc.).</summary>
    SyntaxError,

    /// <summary>The expression references undefined identifiers.</summary>
    InvalidIdentifier,

    /// <summary>The expression exceeds the maximum allowed nesting depth.</summary>
    ExcessiveNesting,

    /// <summary>The expression contains an unsupported construct that cannot be compiled.</summary>
    UnsupportedConstruct
}

/// <summary>
/// Thrown when a condition expression contains a syntax error during parsing.
/// Provides the position of the error, what was expected, and what was found.
/// </summary>
public sealed class ConditionParseException : Exception
{
    /// <summary>Gets the zero-based character position in the expression where the error occurred.</summary>
    public int Position { get; }

    /// <summary>Gets a description of the expected token or construct at the error position.</summary>
    public string Expected { get; }

    /// <summary>Gets a description of what was actually found at the error position.</summary>
    public string Found { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="ConditionParseException"/> with position, expected, and found details.
    /// </summary>
    /// <param name="position">The zero-based character position where the error occurred.</param>
    /// <param name="expected">A description of the expected token or construct.</param>
    /// <param name="found">A description of what was actually found.</param>
    public ConditionParseException(int position, string expected, string found)
        : base($"Parse error at position {position}: expected {expected}, found {found}")
    {
        Position = position;
        Expected = expected;
        Found = found;
    }
}

/// <summary>
/// Thrown when a condition expression references indicator IDs that are not defined
/// in the strategy's indicator configuration list.
/// </summary>
public sealed class ConditionValidationException : Exception
{
    /// <summary>Gets the list of indicator IDs referenced in the expression but not defined in the config.</summary>
    public IReadOnlyList<string> UndefinedReferences { get; }

    /// <summary>Gets the list of indicator IDs that are defined in the strategy config.</summary>
    public IReadOnlyList<string> DefinedIndicatorIds { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="ConditionValidationException"/> with the undefined references
    /// and the list of defined indicator IDs.
    /// </summary>
    /// <param name="undefinedReferences">Indicator IDs referenced but not defined.</param>
    /// <param name="definedIndicatorIds">Indicator IDs that are defined in the config.</param>
    public ConditionValidationException(
        IReadOnlyList<string> undefinedReferences,
        IReadOnlyList<string> definedIndicatorIds)
        : base($"Condition references undefined indicators: [{string.Join(", ", undefinedReferences)}]. " +
               $"Defined indicators: [{string.Join(", ", definedIndicatorIds)}]")
    {
        UndefinedReferences = undefinedReferences;
        DefinedIndicatorIds = definedIndicatorIds;
    }
}
