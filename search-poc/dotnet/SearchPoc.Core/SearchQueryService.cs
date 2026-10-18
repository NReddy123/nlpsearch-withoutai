namespace SearchPoc.Core;

public sealed class InMemorySearchAdapter : ISearchAdapter
{
    private readonly SearchRecord[] _records =
    [
        new("internal-001", "demo", "Acme", "Open", 1500m, new DateOnly(2026, 10, 1)),
        new("internal-002", "demo", "Acme", "Pending", 4200m, new DateOnly(2026, 10, 14)),
        new("internal-003", "demo", "Contoso", "Closed", 900m, new DateOnly(2026, 9, 28)),
        new("internal-004", "other-tenant", "Acme", "Open", 1500m, new DateOnly(2026, 10, 1)),
        new("internal-005", "demo", null, "Open", null, null)
    ];

    public IReadOnlyList<SearchResult> Search(ValidatedQuery query, SearchAccessContext access)
    {
        return _records
            .Where(record => string.Equals(record.TenantId, access.TenantId, StringComparison.Ordinal))
            .Where(record => query.Conditions.All(condition => Matches(record, condition)))
            .Select(record => new SearchResult(record.Customer, record.Status, record.Amount, record.CreatedDate))
            .ToArray();
    }

    private static bool Matches(SearchRecord record, ValidatedCondition condition)
    {
        object? value = condition.Field switch
        {
            "customer" => record.Customer,
            "status" => record.Status,
            "amount" => record.Amount,
            "createdDate" => record.CreatedDate,
            _ => null
        };
        if (value is null)
        {
            return false;
        }

        return condition switch
        {
            ComparisonCondition comparison => Compare(value, comparison.Value, condition.Type, condition.Operator),
            InCondition @in => @in.Values.Any(item => EqualsValue(value, item, condition.Type)),
            RangeCondition range =>
                CompareValues(value, range.LowerBound, condition.Type) >= 0 &&
                CompareValues(value, range.UpperBound, condition.Type) <= 0,
            _ => false
        };
    }

    private static bool Compare(object value, object target, FieldType type, QueryOperator op)
    {
        var comparison = CompareValues(value, target, type);
        return op switch
        {
            QueryOperator.Equal => comparison == 0,
            QueryOperator.NotEqual => comparison != 0,
            QueryOperator.LessThan => comparison < 0,
            QueryOperator.LessThanOrEqual => comparison <= 0,
            QueryOperator.GreaterThan => comparison > 0,
            QueryOperator.GreaterThanOrEqual => comparison >= 0,
            _ => false
        };
    }

    private static bool EqualsValue(object left, object right, FieldType type) =>
        CompareValues(left, right, type) == 0;

    private static int CompareValues(object left, object right, FieldType type) => type switch
    {
        FieldType.String => string.CompareOrdinal((string)left, (string)right),
        FieldType.Number => ((decimal)left).CompareTo((decimal)right),
        FieldType.Date => ((DateOnly)left).CompareTo((DateOnly)right),
        _ => throw new InvalidOperationException($"Unsupported field type '{type}'.")
    };
}

public sealed class SearchQueryService(
    SearchQueryParser parser,
    SearchQueryValidator validator,
    ISearchAdapter adapter)
{
    public SearchResponse Search(string query, SearchAccessContext access)
    {
        var parsed = parser.Parse(query);
        var validated = validator.Validate(parsed);
        return new SearchResponse(validated.Conditions, adapter.Search(validated, access));
    }
}
