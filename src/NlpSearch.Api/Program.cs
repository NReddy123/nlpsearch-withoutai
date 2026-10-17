using System.Text.Json.Serialization;
using NlpSearch.Api.Search;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.MapPost("/api/search/parse", (SearchParseRequest request) =>
{
    try
    {
        var expression = new SearchQueryParser().Parse(request.Query ?? string.Empty);
        return Results.Ok(new SearchParseResponse(expression));
    }
    catch (SearchQueryParseException exception)
    {
        return Results.BadRequest(new SearchParseError(
            "Invalid search query",
            exception.Message,
            exception.Position));
    }
});

app.Run();

public sealed record SearchParseRequest(string? Query);

public sealed record SearchParseResponse(QueryNode Expression);

public sealed record SearchParseError(string Error, string Message, int Position);
