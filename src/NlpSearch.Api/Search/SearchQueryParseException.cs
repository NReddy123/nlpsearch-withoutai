namespace NlpSearch.Api.Search;

public sealed class SearchQueryParseException(string message, int position)
    : Exception(message)
{
    public int Position { get; } = position;
}
