using System.Globalization;
using System.Text.RegularExpressions;

namespace SearchPoc.Core;

public sealed partial class SearchQueryValidator(FieldRegistry registry, QueryLimits? limits = null)
{
    private readonly QueryLimits _limits = limits ?? new QueryLimits();
    private readonly Dictionary<string, FieldDefinition> _fields = BuildLookup(registry);

    public ValidatedQuery Validate(ParsedQuery parsed)
    {
        if (parsed.Conditions.Count > _limits.MaxConditions)
        {
            var offending = parsed.Conditions[_limits.MaxConditions];
            throw new SearchQueryException(
                "too_many_conditions",
                $"Queries may contain at most {_limits.MaxConditions} conditions.",
                offending.Start,
                offending.Length);
        }

        var conditions = new List<ValidatedCondition>(parsed.Conditions.Count);
        foreach (var condition in parsed.Conditions)
        {
            if (!_fields.TryGetValue(condition.Field, out var field))
            {
                throw new SearchQueryException(
                    "unknown_field",
                    $"Unknown search field '{condition.Field}'.",
                    condition.FieldStart,
                    condition.FieldLength);
            }
            if (!field.Operators.Contains(condition.Operator))
            {
                throw new SearchQueryException(
                    "unsupported_operator",
                    $"Operator '{OperatorText(condition.Operator)}' is not allowed for '{field.Name}'.",
                    condition.OperatorStart,
                    condition.OperatorLength);
            }
            if (condition.Operator == QueryOperator.In && condition.Values.Count > _limits.MaxInListSize)
            {
                throw new SearchQueryException(
                    "in_list_too_large",
                    $"IN lists may contain at most {_limits.MaxInListSize} values.",
                    condition.Start,
                    condition.Length);
            }

            var converted = condition.Values.Select(value => ValidateValue(value, field)).ToArray();
            if (condition.Operator == QueryOperator.Range &&
                Compare(field.Type, converted[0], converted[1]) > 0)
            {
                var upper = condition.Values[1];
                throw new SearchQueryException(
                    "invalid_range",
                    "RANGE lower bound must not exceed its upper bound.",
                    upper.Start,
                    upper.Length);
            }

            conditions.Add(condition.Operator switch
            {
                QueryOperator.In => new InCondition(field.Name, field.Type, converted, condition.Start, condition.Length),
                QueryOperator.Range => new RangeCondition(field.Name, field.Type, converted[0], converted[1], condition.Start, condition.Length),
                _ => new ComparisonCondition(field.Name, field.Type, condition.Operator, converted[0], condition.Start, condition.Length)
            });
        }
        return new ValidatedQuery(conditions);
    }

    private static object ValidateValue(RawValue value, FieldDefinition field)
    {
        switch (field.Type)
        {
            case FieldType.String:
                if (!value.Quoted)
                {
                    throw InvalidValue("String values must be double-quoted.", value);
                }
                if (field.AllowedValues is not null &&
                    !field.AllowedValues.Contains(value.Text, StringComparer.Ordinal))
                {
                    throw InvalidValue($"Value '{value.Text}' is not allowed for '{field.Name}'.", value);
                }
                return value.Text;
            case FieldType.Number:
                if (value.Quoted || !NumberPattern().IsMatch(value.Text) ||
                    !decimal.TryParse(value.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw InvalidValue("Expected a representable signed decimal without separators or exponent notation.", value);
                }
                return number;
            case FieldType.Date:
                if (value.Quoted ||
                    !DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw InvalidValue("Expected a valid calendar date in YYYY-MM-DD format.", value);
                }
                return date;
            default:
                throw new InvalidOperationException($"Unsupported field type '{field.Type}'.");
        }
    }

    private static int Compare(FieldType type, object left, object right) => type switch
    {
        FieldType.Number => ((decimal)left).CompareTo((decimal)right),
        FieldType.Date => ((DateOnly)left).CompareTo((DateOnly)right),
        FieldType.String => string.CompareOrdinal((string)left, (string)right),
        _ => throw new InvalidOperationException($"Unsupported field type '{type}'.")
    };

    private static Dictionary<string, FieldDefinition> BuildLookup(FieldRegistry registry)
    {
        var fields = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in registry.Fields)
        {
            fields.Add(field.Name, field);
            foreach (var alias in field.Aliases)
            {
                fields.Add(alias, field);
            }
        }
        return fields;
    }

    private static SearchQueryException InvalidValue(string message, RawValue value) =>
        new("invalid_value", message, value.Start, value.Length);

    public static string OperatorText(QueryOperator queryOperator) => queryOperator switch
    {
        QueryOperator.Equal => "=",
        QueryOperator.NotEqual => "!=",
        QueryOperator.LessThan => "<",
        QueryOperator.LessThanOrEqual => "<=",
        QueryOperator.GreaterThan => ">",
        QueryOperator.GreaterThanOrEqual => ">=",
        QueryOperator.In => "IN",
        QueryOperator.Range => "RANGE",
        _ => throw new ArgumentOutOfRangeException(nameof(queryOperator))
    };

    [GeneratedRegex(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
