namespace SearchPoc.Core;

public enum FieldType
{
    String,
    Number,
    Date
}

public enum QueryOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
    Range
}

public sealed record FieldDefinition(
    string Name,
    IReadOnlyList<string> Aliases,
    FieldType Type,
    IReadOnlyList<QueryOperator> Operators,
    IReadOnlyList<string>? AllowedValues);

public sealed record FieldRegistry(int Version, IReadOnlyList<FieldDefinition> Fields)
{
    public static FieldRegistry Load(string path)
    {
        using var stream = File.OpenRead(path);
        var source = System.Text.Json.JsonSerializer.Deserialize<FieldRegistryFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("Field registry is empty.");
        if (source.Version != 1 || source.Fields.Count == 0)
        {
            throw new InvalidDataException("Field registry must have version 1 and at least one field.");
        }

        var fields = source.Fields.Select(field => new FieldDefinition(
            field.Name,
            field.Aliases,
            Enum.Parse<FieldType>(field.Type, ignoreCase: true),
            field.Operators.Select(ParseOperator).ToArray(),
            field.AllowedValues)).ToArray();
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !identifiers.Add(field.Name))
            {
                throw new InvalidDataException($"Duplicate or empty field name '{field.Name}'.");
            }
            foreach (var alias in field.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) || !identifiers.Add(alias))
                {
                    throw new InvalidDataException($"Duplicate or empty field alias '{alias}'.");
                }
            }
            if (field.Operators.Count == 0 || field.Operators.Distinct().Count() != field.Operators.Count)
            {
                throw new InvalidDataException($"Field '{field.Name}' has missing or duplicate operators.");
            }
        }

        return new FieldRegistry(source.Version, fields);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static QueryOperator ParseOperator(string value) => value.ToUpperInvariant() switch
    {
        "=" => QueryOperator.Equal,
        "!=" => QueryOperator.NotEqual,
        "<" => QueryOperator.LessThan,
        "<=" => QueryOperator.LessThanOrEqual,
        ">" => QueryOperator.GreaterThan,
        ">=" => QueryOperator.GreaterThanOrEqual,
        "IN" => QueryOperator.In,
        "RANGE" => QueryOperator.Range,
        _ => throw new InvalidDataException($"Unknown query operator '{value}' in field registry.")
    };

    private sealed record FieldRegistryFile(int Version, IReadOnlyList<FieldFile> Fields);
    private sealed record FieldFile(
        string Name,
        IReadOnlyList<string> Aliases,
        string Type,
        IReadOnlyList<string> Operators,
        IReadOnlyList<string>? AllowedValues);
}

public sealed record QueryLimits(int MaxLength = 4096, int MaxConditions = 64, int MaxInListSize = 100);

public sealed record QueryError(string Code, string Message, int Start, int Length);

public sealed class SearchQueryException(string code, string message, int start, int length)
    : Exception(message)
{
    public QueryError Error { get; } = new(code, message, start, length);
}

public sealed record RawValue(string Text, bool Quoted, int Start, int Length);

public sealed record RawCondition(
    string Field,
    int FieldStart,
    int FieldLength,
    QueryOperator Operator,
    int OperatorStart,
    int OperatorLength,
    IReadOnlyList<RawValue> Values,
    int Start,
    int Length);

public sealed record ParsedQuery(IReadOnlyList<RawCondition> Conditions);

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ComparisonCondition), "comparison")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(InCondition), "in")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RangeCondition), "range")]
public abstract record ValidatedCondition(
    string Field,
    FieldType Type,
    QueryOperator Operator,
    int Start,
    int Length);

public sealed record ComparisonCondition(
    string Field,
    FieldType Type,
    QueryOperator Operator,
    object Value,
    int Start,
    int Length)
    : ValidatedCondition(Field, Type, Operator, Start, Length);

public sealed record InCondition(
    string Field,
    FieldType Type,
    IReadOnlyList<object> Values,
    int Start,
    int Length)
    : ValidatedCondition(Field, Type, QueryOperator.In, Start, Length);

public sealed record RangeCondition(
    string Field,
    FieldType Type,
    object LowerBound,
    object UpperBound,
    int Start,
    int Length)
    : ValidatedCondition(Field, Type, QueryOperator.Range, Start, Length);

public sealed record ValidatedQuery(IReadOnlyList<ValidatedCondition> Conditions);

public sealed record SearchRecord(
    string RecordId,
    string TenantId,
    string? Customer,
    string? Status,
    decimal? Amount,
    DateOnly? CreatedDate);

public sealed record SearchResult(
    string? Customer,
    string? Status,
    decimal? Amount,
    DateOnly? CreatedDate);

public sealed record SearchAccessContext(string TenantId);

public interface ISearchAdapter
{
    IReadOnlyList<SearchResult> Search(ValidatedQuery query, SearchAccessContext access);
}

public sealed record SearchRequest(string Query);

public sealed record SearchResponse(
    IReadOnlyList<ValidatedCondition> Conditions,
    IReadOnlyList<SearchResult> Results);
