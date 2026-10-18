using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using SearchPoc.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SearchPoc.Lambda;

public sealed class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly FieldRegistry Registry = FieldRegistry.Load(
        Path.Combine(AppContext.BaseDirectory, "shared", "field-registry.json"));
    private static readonly SearchQueryService Service = new(
        new SearchQueryParser(),
        new SearchQueryValidator(Registry),
        new InMemorySearchAdapter());

    public APIGatewayProxyResponse FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var searchRequest = JsonSerializer.Deserialize<SearchRequest>(request.Body ?? string.Empty, JsonOptions);
            if (searchRequest is null)
            {
                throw new SearchQueryException("invalid_request", "A JSON search request is required.", 0, 0);
            }
            var response = Service.Search(searchRequest.Query ?? string.Empty, new SearchAccessContext("demo"));
            return Response(200, response);
        }
        catch (JsonException)
        {
            return Response(400, new QueryError("invalid_request", "Request body must be valid JSON.", 0, 0));
        }
        catch (SearchQueryException exception)
        {
            return Response(400, exception.Error);
        }
    }

    private static APIGatewayProxyResponse Response(int statusCode, object body) => new()
    {
        StatusCode = statusCode,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(body, JsonOptions)
    };
}
