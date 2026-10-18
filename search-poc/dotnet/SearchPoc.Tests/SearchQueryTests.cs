using System.Globalization;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using SearchPoc.Core;
using SearchPoc.Lambda;

namespace SearchPoc.Tests;

public sealed class SearchQueryTests
{
    private static readonly FieldRegistry Registry = FieldRegistry.Load(
        Path.Combine(AppContext.BaseDirectory, "shared", "field-registry.json"));

    [Fact]
    public void Shared_fixtures_have_identical_validated_conditions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "shared", "parser-fixtures.json")));
        var parser = new SearchQueryParser();
        var validator = new SearchQueryValidator(Registry);

        foreach (var fixture in document.RootElement.GetProperty("valid").EnumerateArray())
        {
            var query = fixture.GetProperty("query").GetString()!;
            var actual = validator.Validate(parser.Parse(query));
            var expected = fixture.GetProperty("expected").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, actual.Conditions.Count);
            for (var index = 0; index < expected.Length; index++)
            {
                var item = expected[index];
                var condition = actual.Conditions[index];
                Assert.Equal(item.GetProperty("field").GetString(), condition.Field);
                Assert.Equal(item.GetProperty("type").GetString(), condition.Type.ToString().ToLowerInvariant());
                Assert.Equal(item.GetProperty("operator").GetString(), SearchQueryValidator.OperatorText(condition.Operator));
                Assert.Equal(item.GetProperty("kind").GetString(), condition switch
                {
                    ComparisonCondition => "comparison",
                    InCondition => "in",
                    RangeCondition => "range",
                    _ => throw new InvalidOperationException()
                });
                Assert.Equal(
                    item.GetProperty("values").EnumerateArray().Select(value => value.GetString()).ToArray(),
                    Values(condition));
            }
        }
    }

    [Fact]
    public void Shared_invalid_fixtures_have_matching_error_codes_and_spans()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "shared", "parser-fixtures.json")));
        var parser = new SearchQueryParser();
        var validator = new SearchQueryValidator(Registry);

        foreach (var fixture in document.RootElement.GetProperty("invalid").EnumerateArray())
        {
            var query = fixture.GetProperty("query").GetString()!;
            var exception = Assert.Throws<SearchQueryException>(() => validator.Validate(parser.Parse(query)));
            Assert.Equal(fixture.GetProperty("code").GetString(), exception.Error.Code);
            Assert.Equal(fixture.GetProperty("start").GetInt32(), exception.Error.Start);
            Assert.Equal(fixture.GetProperty("length").GetInt32(), exception.Error.Length);
        }
    }

    [Fact]
    public void Handler_reparses_query_filters_sample_data_and_does_not_leak_internal_ids()
    {
        var handler = new Function();
        var response = handler.FunctionHandler(
            new APIGatewayProxyRequest { Body = "{\"query\":\"status IN (\\\"Open\\\",\\\"Pending\\\") AND amount RANGE (1000, 5000)\"}" },
            new TestLambdaContext());

        Assert.Equal(200, response.StatusCode);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(2, body.RootElement.GetProperty("conditions").GetArrayLength());
        var results = body.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(2, results.Length);
        Assert.False(results[0].TryGetProperty("recordId", out _));
        Assert.False(results[0].TryGetProperty("tenantId", out _));
    }

    [Fact]
    public void Handler_returns_structured_400_for_invalid_query()
    {
        var handler = new Function();
        var response = handler.FunctionHandler(
            new APIGatewayProxyRequest { Body = "{\"query\":\"amount = 1 OR amount = 2\"}" },
            new TestLambdaContext());

        Assert.Equal(400, response.StatusCode);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal("invalid_syntax", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(11, body.RootElement.GetProperty("start").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("length").GetInt32());
    }

    [Fact]
    public void Decimal_and_date_values_are_typed_without_culture_dependency()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var validator = new SearchQueryValidator(Registry);
            var query = new SearchQueryParser().Parse("amount = 1234.50 AND createdDate = 2026-10-01");
            var conditions = validator.Validate(query).Conditions;
            Assert.Equal(1234.50m, Assert.IsType<ComparisonCondition>(conditions[0]).Value);
            Assert.Equal(new DateOnly(2026, 10, 1), Assert.IsType<ComparisonCondition>(conditions[1]).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Configured_in_list_limit_is_enforced()
    {
        var limits = new QueryLimits(MaxInListSize: 2);
        var exception = Assert.Throws<SearchQueryException>(() =>
            new SearchQueryParser(limits).Parse("status IN (\"Open\", \"Pending\", \"Closed\")"));
        Assert.Equal("in_list_too_large", exception.Error.Code);
    }

    [Fact]
    public void Null_values_do_not_match_comparisons_and_tenant_filter_is_independent()
    {
        var service = new SearchQueryService(
            new SearchQueryParser(),
            new SearchQueryValidator(Registry),
            new InMemorySearchAdapter());

        var results = service.Search("amount >= 0", new SearchAccessContext("demo"));

        Assert.Equal(3, results.Results.Count);
        Assert.All(results.Results, result => Assert.NotNull(result.Amount));
    }

    private static string[] Values(ValidatedCondition condition) => condition switch
    {
        ComparisonCondition comparison => [ValueText(comparison.Value)],
        InCondition @in => @in.Values.Select(ValueText).ToArray(),
        RangeCondition range => [ValueText(range.LowerBound), ValueText(range.UpperBound)],
        _ => throw new InvalidOperationException()
    };

    private static string ValueText(object value) => value switch
    {
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new InvalidOperationException()
    };

    private sealed class TestLambdaContext : ILambdaContext
    {
        public string AwsRequestId => "test-request";
        public IClientContext ClientContext => null!;
        public string FunctionName => "search-poc";
        public string FunctionVersion => "1";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:local:0:function:search-poc";
        public ILambdaLogger Logger => new TestLogger();
        public string LogGroupName => "test";
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 128;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(1);

        private sealed class TestLogger : ILambdaLogger
        {
            public void Log(string message) { }
            public void LogLine(string message) { }
        }
    }
}
