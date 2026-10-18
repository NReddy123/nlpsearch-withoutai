namespace SearchPoc.Core;

public sealed class SearchQueryParser(QueryLimits? limits = null)
{
    private readonly QueryLimits _limits = limits ?? new QueryLimits();

    public ParsedQuery Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > _limits.MaxLength)
        {
            throw new SearchQueryException(
                "query_too_long",
                $"Query exceeds {_limits.MaxLength} UTF-16 code units.",
                _limits.MaxLength,
                0);
        }

        var tokens = new SearchQueryTokenizer().Tokenize(query);
        var index = 0;
        Token Current() => tokens[index];
        Token Advance() => tokens[index++];
        Token Require(TokenKind kind, string message)
        {
            if (Current().Kind != kind)
            {
                throw Error("invalid_syntax", message, Current());
            }
            return Advance();
        }

        if (Current().Kind == TokenKind.End)
        {
            throw Error("invalid_syntax", "Enter a search condition.", Current());
        }

        var conditions = new List<RawCondition>();
        while (true)
        {
            var field = Require(TokenKind.Word, "Expected a field name.");
            var operatorToken = Advance();
            if (!TryParseOperator(operatorToken, out var queryOperator))
            {
                throw Error("invalid_syntax", "Expected a supported operator.", operatorToken);
            }

            var values = new List<RawValue>();
            if (queryOperator is QueryOperator.In or QueryOperator.Range)
            {
                Require(TokenKind.LeftParenthesis, $"Expected '(' after {queryOperator.ToString().ToUpperInvariant()}.");
                if (Current().Kind == TokenKind.RightParenthesis)
                {
                    throw Error(
                        "invalid_syntax",
                        queryOperator == QueryOperator.Range ? "RANGE requires exactly two bounds." : "IN requires at least one value.",
                        Current());
                }

                while (true)
                {
                    var token = Current();
                    if (token.Kind is not (TokenKind.Word or TokenKind.Atom or TokenKind.String))
                    {
                        throw Error("invalid_syntax", "Expected a list value.", token);
                    }
                    Advance();
                    values.Add(new RawValue(token.Text, token.Quoted, token.Start, token.Length));
                    if (Current().Kind != TokenKind.Comma)
                    {
                        break;
                    }
                    Advance();
                    if (Current().Kind == TokenKind.RightParenthesis)
                    {
                        throw Error("invalid_syntax", "Expected a value after ','.",                         Current());
                    }
                }
                Require(TokenKind.RightParenthesis, "Expected ')' after the value list.");

                if (queryOperator == QueryOperator.Range && values.Count != 2)
                {
                    var excess = values.Count > 2 ? values[2] : values[^1];
                    throw new SearchQueryException(
                        "invalid_syntax",
                        "RANGE requires exactly two comma-separated bounds.",
                        excess.Start,
                        excess.Length);
                }
                if (queryOperator == QueryOperator.In && values.Count > _limits.MaxInListSize)
                {
                    throw new SearchQueryException(
                        "in_list_too_large",
                        $"IN lists may contain at most {_limits.MaxInListSize} values.",
                        field.Start,
                        field.Length);
                }
            }
            else
            {
                var value = Current();
                if (value.Kind is not (TokenKind.Word or TokenKind.Atom or TokenKind.String))
                {
                    throw Error("invalid_syntax", "Expected a value after the comparison operator.", value);
                }
                Advance();
                values.Add(new RawValue(value.Text, value.Quoted, value.Start, value.Length));
            }

            var last = tokens[index - 1];
            conditions.Add(new RawCondition(
                field.Text,
                field.Start,
                field.Length,
                queryOperator,
                operatorToken.Start,
                operatorToken.Length,
                values,
                field.Start,
                last.Start + last.Length - field.Start));
            if (conditions.Count > _limits.MaxConditions)
            {
                throw new SearchQueryException(
                    "too_many_conditions",
                    $"Queries may contain at most {_limits.MaxConditions} conditions.",
                    field.Start,
                    field.Length);
            }

            if (Current().Kind == TokenKind.End)
            {
                break;
            }
            if (Current().Kind != TokenKind.Word || !string.Equals(Current().Text, "AND", StringComparison.OrdinalIgnoreCase))
            {
                throw Error("invalid_syntax", "Conditions must be joined with explicit AND.", Current());
            }
            Advance();
            if (Current().Kind == TokenKind.End)
            {
                throw Error("invalid_syntax", "Expected a condition after AND.", Current());
            }
        }

        return new ParsedQuery(conditions);
    }

    private static bool TryParseOperator(Token token, out QueryOperator queryOperator)
    {
        queryOperator = token.Text.ToUpperInvariant() switch
        {
            "=" => QueryOperator.Equal,
            "!=" => QueryOperator.NotEqual,
            "<" => QueryOperator.LessThan,
            "<=" => QueryOperator.LessThanOrEqual,
            ">" => QueryOperator.GreaterThan,
            ">=" => QueryOperator.GreaterThanOrEqual,
            "IN" => QueryOperator.In,
            "RANGE" => QueryOperator.Range,
            _ => default
        };
        return token.Kind is TokenKind.Operator or TokenKind.Word
            && token.Text.ToUpperInvariant() is "=" or "!=" or "<" or "<=" or ">" or ">=" or "IN" or "RANGE";
    }

    private static SearchQueryException Error(string code, string message, Token token) =>
        new(code, message, token.Start, token.Length);
}
