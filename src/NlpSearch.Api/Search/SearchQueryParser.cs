namespace NlpSearch.Api.Search;

public sealed class SearchQueryParser
{
    public const int MaximumQueryLength = 4096;
    private const int MaximumTokenCount = 512;
    private const int MaximumNestingDepth = 64;

    public QueryNode Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > MaximumQueryLength)
        {
            throw new SearchQueryParseException(
                $"Query must not exceed {MaximumQueryLength} characters.",
                MaximumQueryLength);
        }

        return new Parser(query).Parse();
    }

    private sealed class Parser
    {
        private readonly string _query;
        private readonly List<Token> _tokens = [];
        private int _tokenIndex;
        private int _nestingDepth;

        public Parser(string query)
        {
            _query = query;
            Tokenize();
        }

        public QueryNode Parse()
        {
            if (Current.Kind == TokenKind.End)
            {
                throw Error("Enter a search term.", Current);
            }

            var expression = ParseOr();
            if (Current.Kind != TokenKind.End)
            {
                throw Error($"Unexpected token '{Current.Value}'.", Current);
            }

            return expression;
        }

        private QueryNode ParseOr()
        {
            var children = new List<QueryNode> { ParseAnd() };
            while (Match(TokenKind.Or))
            {
                children.Add(ParseAnd());
            }

            return children.Count == 1 ? children[0] : new OrNode(children);
        }

        private QueryNode ParseAnd()
        {
            var children = new List<QueryNode> { ParseUnary() };
            while (true)
            {
                if (Match(TokenKind.And))
                {
                    children.Add(ParseUnary());
                }
                else if (StartsUnary(Current.Kind))
                {
                    children.Add(ParseUnary());
                }
                else
                {
                    break;
                }
            }

            return children.Count == 1 ? children[0] : new AndNode(children);
        }

        private QueryNode ParseUnary()
        {
            if (Current.Kind != TokenKind.Not)
            {
                return ParsePrimary();
            }

            var notToken = Current;
            Advance();
            EnterNesting(notToken.Offset);
            try
            {
                return new NotNode(ParseUnary());
            }
            finally
            {
                _nestingDepth--;
            }
        }

        private QueryNode ParsePrimary()
        {
            if (Match(TokenKind.LeftParen))
            {
                EnterNesting(Previous.Offset);
                try
                {
                    var expression = ParseOr();
                    Expect(TokenKind.RightParen, "Expected ')'.");
                    return expression;
                }
                finally
                {
                    _nestingDepth--;
                }
            }

            return ParseTerm();
        }

        private QueryNode ParseTerm()
        {
            var token = Current;
            if (token.Kind is TokenKind.String)
            {
                Advance();
                return new TermNode(null, SearchOperator.Contains, token.Value);
            }

            if (token.Kind != TokenKind.Identifier)
            {
                throw Error("Expected a search term.", token);
            }

            Advance();
            if (!TryGetOperator(Current.Kind, out var searchOperator))
            {
                return new TermNode(null, SearchOperator.Contains, token.Value);
            }

            Advance();
            var value = Current;
            if (!IsValue(value.Kind))
            {
                throw Error("Expected a value after the field operator.", value);
            }

            Advance();
            return new TermNode(token.Value, searchOperator, value.Value);
        }

        private void Tokenize()
        {
            var offset = 0;
            while (offset < _query.Length)
            {
                if (char.IsWhiteSpace(_query[offset]))
                {
                    offset++;
                    continue;
                }

                var start = offset;
                var current = _query[offset++];
                Token token;
                switch (current)
                {
                    case '(':
                        token = new Token(TokenKind.LeftParen, "(", start);
                        break;
                    case ')':
                        token = new Token(TokenKind.RightParen, ")", start);
                        break;
                    case ':':
                        token = new Token(TokenKind.Colon, ":", start);
                        break;
                    case '=':
                        token = new Token(TokenKind.Equal, "=", start);
                        break;
                    case '!':
                        if (offset >= _query.Length || _query[offset] != '=')
                        {
                            throw new SearchQueryParseException("Expected '=' after '!'.", start);
                        }

                        offset++;
                        token = new Token(TokenKind.NotEqual, "!=", start);
                        break;
                    case '>':
                    case '<':
                        var inclusive = offset < _query.Length && _query[offset] == '=';
                        if (inclusive)
                        {
                            offset++;
                        }

                        token = new Token(
                            current switch
                            {
                                '>' when inclusive => TokenKind.GreaterThanOrEqual,
                                '>' => TokenKind.GreaterThan,
                                '<' when inclusive => TokenKind.LessThanOrEqual,
                                _ => TokenKind.LessThan
                            },
                            _query[start..offset],
                            start);
                        break;
                    case '"':
                        token = ReadQuotedString(ref offset, start);
                        break;
                    default:
                        offset = start;
                        token = ReadWord(ref offset);
                        break;
                }

                AddToken(token);
            }

            _tokens.Add(new Token(TokenKind.End, string.Empty, _query.Length));
        }

        private Token ReadQuotedString(ref int offset, int start)
        {
            var value = new System.Text.StringBuilder();
            while (offset < _query.Length)
            {
                var character = _query[offset++];
                if (character == '"')
                {
                    return new Token(TokenKind.String, value.ToString(), start);
                }

                if (character == '\\' && offset < _query.Length)
                {
                    character = _query[offset++];
                }

                value.Append(character);
            }

            throw new SearchQueryParseException("Unterminated quoted phrase.", start);
        }

        private Token ReadWord(ref int offset)
        {
            var start = offset;
            while (offset < _query.Length && !IsDelimiter(_query[offset]))
            {
                offset++;
            }

            if (offset == start)
            {
                throw new SearchQueryParseException(
                    $"Unexpected character '{_query[offset]}'.",
                    offset);
            }

            var value = _query[start..offset];
            var kind = value.ToUpperInvariant() switch
            {
                "AND" => TokenKind.And,
                "OR" => TokenKind.Or,
                "NOT" => TokenKind.Not,
                _ => TokenKind.Identifier
            };

            return new Token(kind, value, start);
        }

        private void AddToken(Token token)
        {
            if (_tokens.Count >= MaximumTokenCount)
            {
                throw new SearchQueryParseException(
                    $"Query must not contain more than {MaximumTokenCount} tokens.",
                    token.Offset);
            }

            _tokens.Add(token);
        }

        private void EnterNesting(int position)
        {
            _nestingDepth++;
            if (_nestingDepth > MaximumNestingDepth)
            {
                _nestingDepth--;
                throw new SearchQueryParseException(
                    $"Query nesting must not exceed {MaximumNestingDepth} levels.",
                    position);
            }
        }

        private void Expect(TokenKind kind, string message)
        {
            if (!Match(kind))
            {
                throw Error(message, Current);
            }
        }

        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind)
            {
                return false;
            }

            Advance();
            return true;
        }

        private void Advance() => _tokenIndex++;

        private Token Current => _tokens[_tokenIndex];

        private Token Previous => _tokens[_tokenIndex - 1];

        private SearchQueryParseException Error(string message, Token token) =>
            new(message, token.Offset);

        private static bool IsValue(TokenKind kind) =>
            kind is TokenKind.Identifier or TokenKind.String or TokenKind.And or TokenKind.Or or TokenKind.Not;

        private static bool StartsUnary(TokenKind kind) =>
            kind is TokenKind.Identifier or TokenKind.String or TokenKind.LeftParen or TokenKind.Not;

        private static bool TryGetOperator(TokenKind kind, out SearchOperator searchOperator)
        {
            searchOperator = kind switch
            {
                TokenKind.Colon => SearchOperator.Contains,
                TokenKind.Equal => SearchOperator.Equal,
                TokenKind.NotEqual => SearchOperator.NotEqual,
                TokenKind.GreaterThan => SearchOperator.GreaterThan,
                TokenKind.GreaterThanOrEqual => SearchOperator.GreaterThanOrEqual,
                TokenKind.LessThan => SearchOperator.LessThan,
                TokenKind.LessThanOrEqual => SearchOperator.LessThanOrEqual,
                _ => default
            };

            return kind is TokenKind.Colon or TokenKind.Equal or TokenKind.NotEqual
                or TokenKind.GreaterThan or TokenKind.GreaterThanOrEqual
                or TokenKind.LessThan or TokenKind.LessThanOrEqual;
        }

        private static bool IsDelimiter(char character) =>
            char.IsWhiteSpace(character) || character is '(' or ')' or ':' or '=' or '!' or '<' or '>' or '"';
    }

    private enum TokenKind
    {
        Identifier,
        String,
        And,
        Or,
        Not,
        LeftParen,
        RightParen,
        Colon,
        Equal,
        NotEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        End
    }

    private readonly record struct Token(TokenKind Kind, string Value, int Offset);
}
