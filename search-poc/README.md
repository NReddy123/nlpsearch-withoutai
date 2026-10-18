# Typed Search POC

This standalone proof of concept demonstrates a small, typed search-expression language from browser input through server-side validation and in-memory search. It lives under `search-poc/` and does not change the existing application. It is an explicit query language, not natural-language search.

The Angular 20 demo uses Vite and `@analogjs/vite-plugin-angular` (not Angular CLI). The TypeScript parser, language-neutral metadata and fixtures, and C# implementation are kept in separate `typescript/`, `shared/`, and `dotnet/` folders. Local search uses sample data; no database, cloud account, or AWS deployment is required.

## Capabilities

- **Typed query language:** string, decimal-number, and calendar-date fields; equality, comparison, `IN`, and inclusive `RANGE` operators where allowed.
- **Explicit Boolean behavior:** conditions are joined with `AND`. `OR`, `NOT`, implicit conjunction, and Boolean grouping are rejected.
- **Registry-driven validation:** one shared JSON registry defines fields, aliases, types, operators, and optional enumerated values. Unknown fields and unsupported operators are rejected.
- **Matching TypeScript and C# implementations:** both tokenize, parse, and validate queries; shared fixtures check accepted conditions and invalid-input error codes and spans across languages.
- **Structured, precise errors:** errors include a code, message, and zero-based UTF-16 `start` and `length`. The Angular UI highlights the reported input span.
- **Interactive Angular client:** debounced local validation, a validated-condition preview, disabled Search action for invalid input, loading and request-error states, and matching result display.
- **Server-side search path:** the API reparses and validates the original submitted query, then applies every condition as an AND against sample records.
- **API Gateway Lambda entry point:** a handler compatible with API Gateway proxy requests returns JSON success or structured 400 responses. It can be tested locally without deployment.
- **Tenant isolation demo:** tenant identity is supplied by server-side demo context, applied independently from user conditions, and omitted along with record IDs from result DTOs.
- **Configurable query limits:** defaults are 4,096 UTF-16 code units, 64 conditions, and 100 values per `IN` list.

## Query language

Each expression is one condition or multiple conditions separated by `AND`. Keywords and field names/aliases are case-insensitive; string values and enumerated values are case-sensitive.

| Field | Alias | Type | Allowed operators | Additional validation |
| --- | --- | --- | --- | --- |
| `customer` | `client` | String | `=`, `IN` | Any double-quoted string |
| `status` | — | String | `=`, `IN` | Must be exactly `Open`, `Pending`, or `Closed` |
| `amount` | — | Number | `=`, `!=`, `<`, `<=`, `>`, `>=`, `IN`, `RANGE` | Signed decimal; no grouping separators or exponent notation |
| `createdDate` | `created` | Date | `=`, `!=`, `<`, `<=`, `>`, `>=`, `IN`, `RANGE` | Valid, unquoted `YYYY-MM-DD` calendar date |

The authoritative allow-list is [field-registry.json](./shared/field-registry.json). It contains public query metadata only—not database mappings or authorization information.

### Operators and values

- Comparison operators apply to one value. String fields currently support equality only.
- `IN` takes one or more comma-separated values in parentheses.
- `RANGE` takes exactly two comma-separated bounds and includes both ends. Equal bounds are valid; the lower bound must not exceed the upper bound.
- String literals must use double quotes. Within them, only quote (`\"`) and backslash (`\\`) are escaped.
- Number values are unquoted signed decimal text. Exponents and grouping separators are not accepted.
- Date values are unquoted exact calendar dates in `YYYY-MM-DD` format.
- Missing/null sample values do not match a condition. String comparisons are ordinal and case-sensitive.

Examples:

```text
customer = "Acme"
client = "Acme"
status IN ("Open", "Pending")
amount > 1000
amount RANGE (1000, 5000)
created RANGE (2026-10-01, 2026-10-14)
status = "Open" AND amount < 5000
```

`OR`, `NOT`, parentheses for Boolean grouping, and adjacent conditions without `AND` are not supported. Parentheses are used only for `IN` and `RANGE` value lists.

## Validation and safety

Parsing and validation are separate steps: the parser produces raw conditions, then registry-driven validation resolves canonical field names and aliases, checks operators and value types, enforces enumerated values and limits, and produces typed conditions. Errors identify the offending span using zero-based UTF-16 code-unit offsets in both implementations.

The browser validates for immediate feedback, but this is not a security boundary: the server reparses and validates the original query before searching. TypeScript keeps numeric text as strings rather than converting it through JavaScript `Number`; C# parses and compares numbers as invariant-culture `decimal`. Dates are checked as exact calendar dates (`DateOnly` in C#).

The sample adapter applies the server-derived tenant filter separately from the user's conditions. The local API and Lambda demo currently use the fixed `"demo"` tenant context; clients cannot choose it in the request. Results expose customer, status, amount, and date, but not internal record or tenant IDs.

For a real integration:

- Derive access context from authenticated server-side identity and enforce authorization independently of user filters.
- Replace `InMemorySearchAdapter` with a data-source adapter that maps only validated, allow-listed conditions to parameterized queries. Never execute query text by concatenating it into a database expression.
- Define the timezone and conversion rules if calendar dates are applied to timestamp-backed data; inclusive date ranges commonly need half-open timestamp intervals.
- Configure production CORS, authentication, deployment, logging, and operational limits for the target environment. The POC's local CORS policy and demo tenant are not production security configuration.

## Components and folder layout

```text
search-poc/
  angular/       Angular standalone app, templates/styles, Vite and compiler configs
  dotnet/
    SearchPoc.Core/   C# tokenizer, parser, validator, service, and in-memory adapter
    SearchPoc.Api/    Local ASP.NET Core HTTP host
    SearchPoc.Lambda/ API Gateway-compatible Lambda handler
    SearchPoc.Tests/  C# tests for shared fixtures, handler, types, and tenant filtering
  shared/        Field registry and cross-language parser fixtures
  typescript/    Browser-independent parser/validator and TypeScript tests
```

The cross-language valid and invalid test cases are in [parser-fixtures.json](./shared/parser-fixtures.json). The Angular app imports the TypeScript parser/validator and the shared registry. The API and Lambda use the C# implementation and the same registry/fixtures.

## HTTP API

The local API exposes `POST /api/search` and accepts the original query as JSON:

```json
{
  "query": "status IN (\"Open\", \"Pending\") AND amount RANGE (1000, 5000)"
}
```

A successful response contains `conditions` (the validated query) and `results` (matching sample records). Invalid JSON/query input returns HTTP 400 with a structured error such as:

```json
{
  "code": "invalid_syntax",
  "message": "Conditions must be joined with explicit AND.",
  "start": 11,
  "length": 2
}
```

`SearchPoc.Lambda.Function.FunctionHandler` accepts an API Gateway proxy request with the same JSON body and returns JSON with status 200 or 400. The Lambda project references the AWS Lambda Core, API Gateway events, and System.Text.Json serializer packages. Deploying it is optional and is not covered by the local POC.

## Run locally

Requires Node.js 22.6 or newer and a .NET SDK that can target `net8.0` (SDK 8 or later). Run commands from the repository root.

### Install, test, and type-check TypeScript

```powershell
npm install --prefix .\search-poc
npm run test:typescript --prefix .\search-poc
npm run typecheck:typescript --prefix .\search-poc
```

### Build the Angular demo

```powershell
npm run build:angular --prefix .\search-poc
```

The build runs Angular's compiler checks before producing a Vite production bundle.

### Run the API and Angular client

Start the API in one terminal:

```powershell
dotnet run --project .\search-poc\dotnet\SearchPoc.Api\SearchPoc.Api.csproj --urls http://127.0.0.1:5290
```

Start the Angular development server in another terminal:

```powershell
npm run start:angular --prefix .\search-poc
```

Open `http://localhost:4200`. Vite proxies `/api` requests to the local API. The API's development CORS policy permits `http://localhost:4200` and `http://127.0.0.1:4200`.

### Build and test C#

```powershell
dotnet test .\search-poc\dotnet\SearchPoc.Tests\SearchPoc.Tests.csproj
dotnet build .\search-poc\dotnet\SearchPoc.Api\SearchPoc.Api.csproj
```

The C# tests load the shared registry and fixtures, verify matching parser results/error spans, check culture-independent number/date handling and tenant filtering, and invoke the Lambda handler directly.
