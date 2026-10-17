using System.Text.Json.Serialization;

namespace NlpSearch.Api.Search;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TermNode), "term")]
[JsonDerivedType(typeof(AndNode), "and")]
[JsonDerivedType(typeof(OrNode), "or")]
[JsonDerivedType(typeof(NotNode), "not")]
public abstract record QueryNode;

public sealed record TermNode(string? Field, SearchOperator Operator, string Value) : QueryNode;

public sealed record AndNode(IReadOnlyList<QueryNode> Children) : QueryNode;

public sealed record OrNode(IReadOnlyList<QueryNode> Children) : QueryNode;

public sealed record NotNode(QueryNode Child) : QueryNode;

public enum SearchOperator
{
    Contains,
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}
