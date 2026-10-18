using System.Text.Json.Serialization;
using SearchPoc.Core;

var builder = WebApplication.CreateBuilder(args);
var registryPath = Path.Combine(AppContext.BaseDirectory, "shared", "field-registry.json");
var registry = FieldRegistry.Load(registryPath);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton(new SearchQueryParser());
builder.Services.AddSingleton(new SearchQueryValidator(registry));
builder.Services.AddSingleton<ISearchAdapter, InMemorySearchAdapter>();
builder.Services.AddSingleton<SearchQueryService>();
builder.Services.AddCors(options => options.AddPolicy(
    "angular-demo",
    policy => policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors("angular-demo");

app.MapPost("/api/search", (SearchRequest request, SearchQueryService service) =>
{
    try
    {
        var response = service.Search(request.Query ?? string.Empty, new SearchAccessContext("demo"));
        return Results.Ok(response);
    }
    catch (SearchQueryException exception)
    {
        return Results.BadRequest(exception.Error);
    }
});

app.Run();
