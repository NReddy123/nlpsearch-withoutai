using NlpSearch.Api.Search;

namespace NlpSearch.Api.Tests;

public sealed class SearchQueryParserTests
{
    private readonly SearchQueryParser _parser = new();

    [Fact]
    public void Parse_uses_not_then_and_then_or_precedence()
    {
        var result = _parser.Parse(
            "status:open OR title:\"release notes\" AND NOT archived:true");

        var disjunction = Assert.IsType<OrNode>(result);
        Assert.Equal(2, disjunction.Children.Count);
        Assert.Equal(
            new TermNode("status", SearchOperator.Contains, "open"),
            Assert.IsType<TermNode>(disjunction.Children[0]));

        var conjunction = Assert.IsType<AndNode>(disjunction.Children[1]);
        Assert.Equal(2, conjunction.Children.Count);
        Assert.Equal(
            new TermNode("title", SearchOperator.Contains, "release notes"),
            Assert.IsType<TermNode>(conjunction.Children[0]));

        var negation = Assert.IsType<NotNode>(conjunction.Children[1]);
        Assert.Equal(
            new TermNode("archived", SearchOperator.Contains, "true"),
            Assert.IsType<TermNode>(negation.Child));
    }

    [Fact]
    public void Parse_treats_adjacent_terms_as_and()
    {
        var result = Assert.IsType<AndNode>(_parser.Parse("alpha beta AND gamma"));

        Assert.Equal(3, result.Children.Count);
        Assert.All(result.Children, child => Assert.IsType<TermNode>(child));
    }

    [Theory]
    [InlineData("price=10", SearchOperator.Equal)]
    [InlineData("price!=10", SearchOperator.NotEqual)]
    [InlineData("price>10", SearchOperator.GreaterThan)]
    [InlineData("price>=10", SearchOperator.GreaterThanOrEqual)]
    [InlineData("price<10", SearchOperator.LessThan)]
    [InlineData("price<=10", SearchOperator.LessThanOrEqual)]
    [InlineData("price:10", SearchOperator.Contains)]
    public void Parse_supports_field_comparison_operators(
        string query,
        SearchOperator expectedOperator)
    {
        var result = Assert.IsType<TermNode>(_parser.Parse(query));

        Assert.Equal("price", result.Field);
        Assert.Equal(expectedOperator, result.Operator);
        Assert.Equal("10", result.Value);
    }

    [Fact]
    public void Parse_supports_quoted_phrases_and_escaped_quotes()
    {
        var result = Assert.IsType<TermNode>(
            _parser.Parse("title:\"A \\\"quoted\\\" phrase\""));

        Assert.Equal("title", result.Field);
        Assert.Equal("A \"quoted\" phrase", result.Value);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("title:", 6)]
    [InlineData("(title:open", 11)]
    [InlineData("title:open)", 10)]
    [InlineData("\"unfinished", 0)]
    public void Parse_reports_syntax_errors_with_source_positions(string query, int position)
    {
        var exception = Assert.Throws<SearchQueryParseException>(() => _parser.Parse(query));

        Assert.Equal(position, exception.Position);
    }

    [Fact]
    public void Parse_rejects_excessive_input_length()
    {
        var query = new string('a', SearchQueryParser.MaximumQueryLength + 1);

        var exception = Assert.Throws<SearchQueryParseException>(() => _parser.Parse(query));

        Assert.Equal(SearchQueryParser.MaximumQueryLength, exception.Position);
    }

    [Fact]
    public void Parse_rejects_excessive_token_count()
    {
        var query = string.Join(' ', Enumerable.Repeat("word", 513));

        var exception = Assert.Throws<SearchQueryParseException>(() => _parser.Parse(query));

        Assert.Contains("tokens", exception.Message);
    }

    [Fact]
    public void Parse_rejects_excessive_nesting()
    {
        var query = new string('(', 65) + "term" + new string(')', 65);

        var exception = Assert.Throws<SearchQueryParseException>(() => _parser.Parse(query));

        Assert.Contains("nesting", exception.Message);
    }
}
