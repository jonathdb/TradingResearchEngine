namespace TradingResearchEngine.Application.Strategies.Composite.Conditions;

/// <summary>
/// Recursive-descent parser for the condition expression language.
/// Produces an AST from a condition string following the grammar:
/// <code>
/// expression     → logical_or
/// logical_or     → logical_and ( "OR" logical_and )*
/// logical_and    → primary ( "AND" primary )*
/// primary        → comparison | cross_call | "(" expression ")"
/// comparison     → value comp_op value
/// cross_call     → ("crosses_above" | "crosses_below") "(" value "," value ")"
/// comp_op        → ">" | "&lt;" | ">=" | "&lt;=" | "==" | "!="
/// value          → indicator_ref | price_ref | number
/// indicator_ref  → IDENTIFIER ( "." IDENTIFIER )?
/// price_ref      → "open" | "high" | "low" | "close" | "volume"
/// number         → ["-"] DIGIT+ ["." DIGIT+]
/// IDENTIFIER     → LETTER (LETTER | DIGIT | "_")*
/// </code>
/// </summary>
public static class ConditionParser
{
    /// <summary>
    /// Parses a condition expression string into an AST.
    /// </summary>
    /// <param name="expression">The condition expression to parse.</param>
    /// <returns>The root <see cref="ConditionNode"/> of the parsed AST.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expression"/> is null.</exception>
    /// <exception cref="ConditionParseException">Thrown when the expression contains a syntax error.</exception>
    public static ConditionNode Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var tokeniser = new Tokeniser(expression);
        var tokens = tokeniser.Tokenise();
        var parser = new Parser(tokens, expression);
        var ast = parser.ParseExpression();
        parser.ExpectEnd();
        return ast;
    }
}

#region Token Types

/// <summary>
/// Represents the type of a lexical token in the condition expression language.
/// </summary>
internal enum TokenType
{
    Identifier,
    Number,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Equal,
    NotEqual,
    LeftParen,
    RightParen,
    Comma,
    Dot,
    And,
    Or,
    CrossesAbove,
    CrossesBelow,
    End
}

/// <summary>
/// Represents a single lexical token with its type, text value, and position in the source.
/// </summary>
internal readonly record struct Token(TokenType Type, string Value, int Position);

#endregion

#region Tokeniser

/// <summary>
/// Lexical analyser that converts a condition expression string into a sequence of tokens.
/// </summary>
internal sealed class Tokeniser
{
    private readonly string _source;
    private int _pos;
    private readonly List<Token> _tokens = new();

    private static readonly HashSet<string> PriceKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "high", "low", "close", "volume"
    };

    public Tokeniser(string source)
    {
        _source = source;
        _pos = 0;
    }

    public List<Token> Tokenise()
    {
        while (_pos < _source.Length)
        {
            SkipWhitespace();
            if (_pos >= _source.Length)
                break;

            var ch = _source[_pos];

            if (char.IsLetter(ch) || ch == '_')
            {
                ReadIdentifierOrKeyword();
            }
            else if (char.IsDigit(ch))
            {
                ReadNumber();
            }
            else if (ch == '-')
            {
                // Negative number: only if preceded by an operator, comma, left paren, or at start
                if (IsNegativeNumberContext())
                {
                    ReadNumber();
                }
                else
                {
                    throw new ConditionParseException(_pos, "valid token", $"'{ch}'");
                }
            }
            else
            {
                ReadOperatorOrPunctuation();
            }
        }

        _tokens.Add(new Token(TokenType.End, "", _pos));
        return _tokens;
    }

    private bool IsNegativeNumberContext()
    {
        // A minus sign starts a negative number if:
        // - It's the first token
        // - The previous token is an operator, comma, or left paren
        if (_tokens.Count == 0)
            return true;

        var lastType = _tokens[^1].Type;
        return lastType is TokenType.GreaterThan or TokenType.LessThan
            or TokenType.GreaterThanOrEqual or TokenType.LessThanOrEqual
            or TokenType.Equal or TokenType.NotEqual
            or TokenType.LeftParen or TokenType.Comma;
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos]))
            _pos++;
    }

    private void ReadIdentifierOrKeyword()
    {
        var start = _pos;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
            _pos++;

        var text = _source[start.._pos];

        // Check for keywords (case-insensitive)
        if (string.Equals(text, "AND", StringComparison.OrdinalIgnoreCase))
        {
            _tokens.Add(new Token(TokenType.And, text, start));
        }
        else if (string.Equals(text, "OR", StringComparison.OrdinalIgnoreCase))
        {
            _tokens.Add(new Token(TokenType.Or, text, start));
        }
        else if (string.Equals(text, "crosses_above", StringComparison.OrdinalIgnoreCase))
        {
            _tokens.Add(new Token(TokenType.CrossesAbove, text, start));
        }
        else if (string.Equals(text, "crosses_below", StringComparison.OrdinalIgnoreCase))
        {
            _tokens.Add(new Token(TokenType.CrossesBelow, text, start));
        }
        else
        {
            _tokens.Add(new Token(TokenType.Identifier, text, start));
        }
    }

    private void ReadNumber()
    {
        var start = _pos;

        // Optional leading minus
        if (_pos < _source.Length && _source[_pos] == '-')
            _pos++;

        if (_pos >= _source.Length || !char.IsDigit(_source[_pos]))
            throw new ConditionParseException(start, "digit after '-'", _pos < _source.Length ? $"'{_source[_pos]}'" : "end of expression");

        while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            _pos++;

        // Optional decimal part
        if (_pos < _source.Length && _source[_pos] == '.')
        {
            _pos++;
            if (_pos >= _source.Length || !char.IsDigit(_source[_pos]))
                throw new ConditionParseException(start, "digit after '.'", _pos < _source.Length ? $"'{_source[_pos]}'" : "end of expression");

            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
                _pos++;
        }

        var text = _source[start.._pos];
        _tokens.Add(new Token(TokenType.Number, text, start));
    }

    private void ReadOperatorOrPunctuation()
    {
        var start = _pos;
        var ch = _source[_pos];

        switch (ch)
        {
            case '(':
                _tokens.Add(new Token(TokenType.LeftParen, "(", start));
                _pos++;
                break;
            case ')':
                _tokens.Add(new Token(TokenType.RightParen, ")", start));
                _pos++;
                break;
            case ',':
                _tokens.Add(new Token(TokenType.Comma, ",", start));
                _pos++;
                break;
            case '.':
                _tokens.Add(new Token(TokenType.Dot, ".", start));
                _pos++;
                break;
            case '>':
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    _tokens.Add(new Token(TokenType.GreaterThanOrEqual, ">=", start));
                    _pos++;
                }
                else
                {
                    _tokens.Add(new Token(TokenType.GreaterThan, ">", start));
                }
                break;
            case '<':
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    _tokens.Add(new Token(TokenType.LessThanOrEqual, "<=", start));
                    _pos++;
                }
                else
                {
                    _tokens.Add(new Token(TokenType.LessThan, "<", start));
                }
                break;
            case '=':
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    _tokens.Add(new Token(TokenType.Equal, "==", start));
                    _pos++;
                }
                else
                {
                    throw new ConditionParseException(start, "'=='", $"'={(_pos < _source.Length ? _source[_pos].ToString() : "")}'");
                }
                break;
            case '!':
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '=')
                {
                    _tokens.Add(new Token(TokenType.NotEqual, "!=", start));
                    _pos++;
                }
                else
                {
                    throw new ConditionParseException(start, "'!='", $"'!{(_pos < _source.Length ? _source[_pos].ToString() : "")}'");
                }
                break;
            default:
                throw new ConditionParseException(start, "valid token", $"'{ch}'");
        }
    }
}

#endregion

#region Parser

/// <summary>
/// Recursive-descent parser that converts a token stream into a condition AST.
/// </summary>
internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly string _source;
    private int _pos;

    private static readonly HashSet<string> PriceKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "high", "low", "close", "volume"
    };

    public Parser(List<Token> tokens, string source)
    {
        _tokens = tokens;
        _source = source;
        _pos = 0;
    }

    private Token Current => _tokens[_pos];

    private Token Advance()
    {
        var token = _tokens[_pos];
        _pos++;
        return token;
    }

    private Token Expect(TokenType type, string description)
    {
        if (Current.Type != type)
        {
            throw new ConditionParseException(
                Current.Position,
                description,
                Current.Type == TokenType.End ? "end of expression" : $"'{Current.Value}'");
        }
        return Advance();
    }

    /// <summary>
    /// Ensures the parser has consumed all tokens.
    /// </summary>
    public void ExpectEnd()
    {
        if (Current.Type != TokenType.End)
        {
            throw new ConditionParseException(
                Current.Position,
                "end of expression",
                $"'{Current.Value}'");
        }
    }

    /// <summary>
    /// Parses the top-level expression (logical_or).
    /// </summary>
    public ConditionNode ParseExpression()
    {
        return ParseLogicalOr();
    }

    /// <summary>
    /// logical_or → logical_and ( "OR" logical_and )*
    /// </summary>
    private ConditionNode ParseLogicalOr()
    {
        var left = ParseLogicalAnd();

        while (Current.Type == TokenType.Or)
        {
            Advance();
            var right = ParseLogicalAnd();
            left = new LogicalNode(left, LogicalOperator.Or, right);
        }

        return left;
    }

    /// <summary>
    /// logical_and → primary ( "AND" primary )*
    /// </summary>
    private ConditionNode ParseLogicalAnd()
    {
        var left = ParsePrimary();

        while (Current.Type == TokenType.And)
        {
            Advance();
            var right = ParsePrimary();
            left = new LogicalNode(left, LogicalOperator.And, right);
        }

        return left;
    }

    /// <summary>
    /// primary → cross_call | comparison | "(" expression ")"
    /// </summary>
    private ConditionNode ParsePrimary()
    {
        // Check for cross_call first (resolves ambiguity with identifiers)
        if (Current.Type is TokenType.CrossesAbove or TokenType.CrossesBelow)
        {
            return ParseCrossCall();
        }

        // Check for parenthesised expression
        if (Current.Type == TokenType.LeftParen)
        {
            Advance();
            var expr = ParseExpression();
            Expect(TokenType.RightParen, "')'");
            return expr;
        }

        // Otherwise, parse comparison: value comp_op value
        return ParseComparison();
    }

    /// <summary>
    /// cross_call → ("crosses_above" | "crosses_below") "(" value "," value ")"
    /// </summary>
    private ConditionNode ParseCrossCall()
    {
        var crossToken = Advance();
        var direction = crossToken.Type == TokenType.CrossesAbove
            ? CrossDirection.Above
            : CrossDirection.Below;

        Expect(TokenType.LeftParen, "'(' after crosses function");
        var left = ParseValue();
        Expect(TokenType.Comma, "',' separating arguments");
        var right = ParseValue();
        Expect(TokenType.RightParen, "')' closing crosses function");

        return new CrossNode(left, right, direction);
    }

    /// <summary>
    /// comparison → value comp_op value
    /// </summary>
    private ConditionNode ParseComparison()
    {
        var left = ParseValue();
        var op = ParseComparisonOperator();
        var right = ParseValue();
        return new ComparisonNode(left, op, right);
    }

    /// <summary>
    /// comp_op → ">" | "&lt;" | ">=" | "&lt;=" | "==" | "!="
    /// </summary>
    private ComparisonOperator ParseComparisonOperator()
    {
        var token = Current;
        return token.Type switch
        {
            TokenType.GreaterThan => Consume(ComparisonOperator.GreaterThan),
            TokenType.LessThan => Consume(ComparisonOperator.LessThan),
            TokenType.GreaterThanOrEqual => Consume(ComparisonOperator.GreaterThanOrEqual),
            TokenType.LessThanOrEqual => Consume(ComparisonOperator.LessThanOrEqual),
            TokenType.Equal => Consume(ComparisonOperator.Equal),
            TokenType.NotEqual => Consume(ComparisonOperator.NotEqual),
            _ => throw new ConditionParseException(
                token.Position,
                "comparison operator (>, <, >=, <=, ==, !=)",
                token.Type == TokenType.End ? "end of expression" : $"'{token.Value}'")
        };

        ComparisonOperator Consume(ComparisonOperator op)
        {
            Advance();
            return op;
        }
    }

    /// <summary>
    /// value → number | price_ref | indicator_ref
    /// </summary>
    private ValueNode ParseValue()
    {
        var token = Current;

        // Number literal
        if (token.Type == TokenType.Number)
        {
            Advance();
            var value = double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture);
            return new LiteralNode(value);
        }

        // Identifier: could be price_ref or indicator_ref (with optional dot notation)
        if (token.Type == TokenType.Identifier)
        {
            Advance();
            var identifier = token.Value;

            // Check if it's a price keyword
            if (IsPriceKeyword(identifier))
            {
                var field = ParsePriceField(identifier);
                return new PriceRefNode(field);
            }

            // It's an indicator reference — check for dot notation
            if (Current.Type == TokenType.Dot)
            {
                Advance();
                var subToken = Expect(TokenType.Identifier, "sub-property name after '.'");
                return new IndicatorRefNode(identifier, subToken.Value);
            }

            return new IndicatorRefNode(identifier);
        }

        throw new ConditionParseException(
            token.Position,
            "value (identifier, number, or price reference)",
            token.Type == TokenType.End ? "end of expression" : $"'{token.Value}'");
    }

    private static bool IsPriceKeyword(string identifier)
    {
        return PriceKeywords.Contains(identifier);
    }

    private static PriceField ParsePriceField(string identifier)
    {
        return identifier.ToLowerInvariant() switch
        {
            "open" => PriceField.Open,
            "high" => PriceField.High,
            "low" => PriceField.Low,
            "close" => PriceField.Close,
            "volume" => PriceField.Volume,
            _ => throw new InvalidOperationException($"Unknown price field: {identifier}")
        };
    }
}

#endregion
