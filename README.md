# NLP Search Without AI

A .NET 8 API for parsing human-written advanced search expressions without an LLM, NLP service, or one large regular expression. It tokenizes input and builds a typed query tree with a recursive-descent parser.

## Run

```sh
dotnet run --project src/NlpSearch.Api
```

Send search text to `POST /api/search/parse`:

```sh
curl -X POST http://localhost:5284/api/search/parse \
  -H 'Content-Type: application/json' \
  -d '{"query":"status:open AND (title:\"release notes\" OR created>=2026-01-01)"}'
```

The API returns a JSON expression tree, for example:

```json
{
  "expression": {
    "kind": "and",
    "children": [
      { "kind": "term", "field": "status", "operator": "Contains", "value": "open" },
      {
        "kind": "or",
        "children": [
          { "kind": "term", "field": "title", "operator": "Contains", "value": "release notes" },
          { "kind": "term", "field": "created", "operator": "GreaterThanOrEqual", "value": "2026-01-01" }
        ]
      }
    ]
  }
}
```

Invalid input returns HTTP 400 with an error message and zero-based character position:

```json
{ "error": "Invalid search query", "message": "Expected ')'.", "position": 14 }
```

## Query syntax

| Syntax | Meaning |
| --- | --- |
| `word` or `"exact phrase"` | Unqualified contains term |
| `field:value` | Field contains value |
| `field=value`, `field!=value` | Equality or inequality |
| `field>value`, `field>=value`, `field<value`, `field<=value` | Comparisons |
| `term AND term` or adjacent terms | Conjunction |
| `term OR term` | Disjunction |
| `NOT term` | Negation |
| `(expression)` | Grouping |

`NOT` binds most tightly, then `AND`, then `OR`. Adjacent terms imply `AND`. Keywords are case-insensitive; quoted phrases may contain spaces and escaped characters. Input is limited to 4,096 characters, 512 tokens, and 64 nested expressions.

## Frontend integration

The endpoint accepts ordinary JSON, so it can be called from either Angular or React. Use your deployed API URL (or a same-origin development proxy); configure CORS in the API host when the browser and API use different origins.

```ts
export async function parseSearch(query: string) {
  const response = await fetch('/api/search/parse', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ query })
  });

  const result = await response.json();
  if (!response.ok) {
    throw new Error(`${result.message} at character ${result.position}`);
  }
  return result.expression;
}
```

Call `parseSearch(searchText)` from an Angular service or a React event handler, then use the returned tree for validation or a query preview. This starter exposes parsing only: the repository has no search entity, field schema, or data store to query. To return search results, the host application should validate fields against an allow-list and translate this AST to parameterized data-store queries; do not concatenate AST values into SQL.

## Test

```sh
dotnet test
```
