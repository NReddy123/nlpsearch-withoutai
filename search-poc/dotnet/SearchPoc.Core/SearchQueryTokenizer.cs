namespace SearchPoc.Core;

internal enum TokenKind
{
    Word,
    Atom,
    String,
    Operator,
    Comma,
    LeftParenthesis,
    RightParenthesis,
    End
}

internal sealed record Token(TokenKind Kind, string Text, bool Quoted, int Start, int Length);

internal sealed class SearchQueryTokenizer
{
    public IReadOnlyList<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < query.Length)
        {
            if (char.IsWhiteSpace(query[index]))
            {
                index++;
                continue;
            }

            var start = index;
            var current = query[index];
            if (current is '(' or ')' or ',')
            {
                index++;
                tokens.Add(new Token(
                    current switch
                    {
                        '(' => TokenKind.LeftParenthesis,
                        ')' => TokenKind.RightParenthesis,
                        _ => TokenKind.Comma
                    },
                    current.ToString(),
                    false,
                    start,
                    1));
                continue;
            }

            if (current is '=' or '!' or '<' or '>')
            {
                index++;
                if (index < query.Length && query[index] == '=' && current != '=')
                {
                    index++;
                }
                var text = query[start..index];
                if (text is not ("=" or "!=" or "<" or "<=" or ">" or ">="))
                {
                    throw Error("invalid_syntax", "Unsupported operator.", start, text.Length);
                }
                tokens.Add(new Token(TokenKind.Operator, text, false, start, text.Length));
                continue;
            }

            if (current == '"')
            {
                index++;
                var value = new System.Text.StringBuilder();
                var closed = false;
                while (index < query.Length)
                {
                    var character = query[index++];
                    if (character == '"')
                    {
                        closed = true;
                        break;
                    }
                    if (character == '\\')
                    {
                        if (index >= query.Length || query[index] is not ('\\' or '"'))
                        {
                            throw Error(
                                "invalid_syntax",
                                "Only \\\\ and \\\" escapes are supported in quoted strings.",
                                index - 1,
                                Math.Min(2, query.Length - (index - 1)));
                        }
                        character = query[index++];
                    }
                    value.Append(character);
                }

                if (!closed)
                {
                    throw Error("invalid_syntax", "Unterminated quoted string.", start, query.Length - start);
                }

                tokens.Add(new Token(TokenKind.String, value.ToString(), true, start, index - start));
                continue;
            }

            while (index < query.Length &&
                   !char.IsWhiteSpace(query[index]) &&
                   query[index] is not (',' or '(' or ')' or '=' or '!' or '<' or '>' or '"'))
            {
                index++;
            }
            if (index == start)
            {
                throw Error("invalid_syntax", $"Unexpected character '{current}'.", start, 1);
            }

            var atom = query[start..index];
            var isWord = atom.Length > 0 &&
                (char.IsAsciiLetter(atom[0]) || atom[0] == '_') &&
                atom.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
            tokens.Add(new Token(isWord ? TokenKind.Word : TokenKind.Atom, atom, false, start, atom.Length));
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, false, query.Length, 0));
        return tokens;
    }

    private static SearchQueryException Error(string code, string message, int start, int length) =>
        new(code, message, start, length);
}
