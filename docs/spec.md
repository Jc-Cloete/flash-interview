# Sensitive Words Interview Project Specification

## 1. Purpose

Build a small service that allows a client chat application to submit a message and receive the same message with configured sensitive words replaced by `*` characters.

The interview brief uses SQL keywords as the initial sensitive-word set, but the service must support any company-defined sensitive terms, including profanity, internal terms, phrases, or future categories.

## 2. Source Material

This specification is based on:

- `docs/Interview-SqlWords.pdf`
- `docs/sql_sensitive_list.txt`
- Additional email requirements from the interviewer:
  - RESTful API in C# .NET Core.
  - Swagger documentation with documented endpoints, parameters, and responses.
  - Database CRUD layer backed by MSSQL.
  - Simple ASP.NET MVC frontend with an Admin page and mock Chat page.
  - Frontend must not connect directly to the database.
  - Include performance and completeness enhancement discussion.

The preload file contains 228 sensitive entries, beginning with `ACTION`, `ADD`, `ALL`, and ending with `STRINGTABLE`, `LONGTABLE`, `DOUBLETABLE`, and `SELECT * FROM`.

## 3. Scenario

1. A user sends a message in the client application.
2. The client application sends the message to the Sensitive Words API.
3. The API loads the configured sensitive words from the database.
4. The API replaces sensitive words in the message with `*` characters.
5. The API returns the amended message.
6. The client application displays the amended message.

Example from the brief:

```text
Input:  SELECT * FROM sensitiveWords
Output: ****** * FROM sensitiveWords
```

Implementation note: the example masks `SELECT` but leaves `FROM` visible, while the preload list contains many SQL terms and also contains `SELECT * FROM`. Unless we intentionally choose example-specific behavior later, the project should treat the database as the source of truth and mask every configured term.

## 4. Goals

- Implement the solution in C# .NET Core.
- Provide CRUD endpoints for managing sensitive words for internal consumers.
- Provide one external business-logic endpoint that accepts a message and returns a masked message.
- Store sensitive words in MSSQL.
- Generate Swagger/OpenAPI documentation for all endpoints.
- Provide a simple ASP.NET MVC frontend with:
  - An Admin page for managing sensitive words through the API.
  - A mock Chat page that demonstrates message blooping through the API.
- Preload the supplied SQL-sensitive list into the database.
- Provide a production deployment walkthrough.
- Document performance improvements and additional enhancements.
- Keep the solution simple enough for an interview submission while still showing clean design, validation, testing, and deployment thinking.

## 5. Non-Goals

- Build a full chat client.
- Build user account management unless needed for API protection.
- Let the frontend connect directly to MSSQL.
- Implement complex natural-language moderation or machine-learning classification.

## 6. Chosen Technology Stack

The project should use the stack requested by the interviewer:

| Layer | Choice | Notes |
| --- | --- | --- |
| API | C# .NET Core / ASP.NET Core Web API | RESTful API for CRUD and masking behavior. |
| UI | ASP.NET Core MVC | Simple server-rendered frontend for the admin and mock chat pages. |
| Database | Microsoft SQL Server | Required database backend for sensitive-word persistence. |
| Data access | Entity Framework Core | Recommended for migrations, CRUD, and LINQ-based queries. |
| API docs | Swagger/OpenAPI | Use Swashbuckle or ASP.NET Core OpenAPI support, with endpoint annotations. |
| Tests | xUnit or NUnit | Unit tests for masking logic and integration tests for API/database behavior. |
| Local environment | Docker Compose | Run MSSQL locally in a repeatable way. |

## 7. Core Concepts

### Sensitive Word

A configured term that should be masked when it appears in a submitted message.

Recommended fields:

| Field | Type | Notes |
| --- | --- | --- |
| `id` | UUID/string | Stable identifier. |
| `value` | string | The exact term or phrase to match. |
| `normalizedValue` | string | Canonical value used for uniqueness and matching. |
| `category` | string/null | Optional grouping, for example `sql`, `profanity`, `internal`. |
| `isActive` | boolean | Inactive terms remain stored but are not applied. |
| `createdAt` | timestamp | Audit metadata. |
| `updatedAt` | timestamp | Audit metadata. |

### Masked Message

A message where matched sensitive terms are replaced by stars. The default behavior should preserve the matched term length:

```text
DROP -> ****
SELECT -> ******
```

## 8. Matching Rules

Initial implementation should use deterministic string matching, not AI moderation.

Required behavior:

- Matching is case-insensitive.
- Replacement uses one `*` per character in the matched term.
- Single-word entries should match on word boundaries so `DROP` does not mask part of `DROPLET`.
- Phrase entries such as `SELECT * FROM` should be supported.
- When multiple entries overlap, the longest match should win.
- Only active sensitive words should be applied.
- The original message casing and spacing should be preserved outside masked spans.

Open decision:

- Whether SQL keywords should all be masked from the preload list, or whether only selected terms such as `SELECT` and `DROP` should be enabled by default. The brief says the API combines the message with data in the DB, so the recommended default is to preload all supplied entries as active.

## 9. API Requirements

The API must be RESTful and implemented with C# .NET Core. All endpoints must be included in Swagger/OpenAPI documentation with clear summaries, request schemas, response schemas, and relevant status codes.

### Internal CRUD Endpoints

These endpoints are for internal consumers or administrators.

#### Create Sensitive Word

`POST /api/sensitive-words`

Swagger documentation should describe this endpoint as creating a new sensitive word for internal/admin use.

Request:

```json
{
  "value": "DROP",
  "category": "sql",
  "isActive": true
}
```

Response: `201 Created`

```json
{
  "id": "uuid",
  "value": "DROP",
  "normalizedValue": "drop",
  "category": "sql",
  "isActive": true,
  "createdAt": "2026-05-09T00:00:00.000Z",
  "updatedAt": "2026-05-09T00:00:00.000Z"
}
```

#### List Sensitive Words

`GET /api/sensitive-words`

Swagger documentation should describe query parameters and pagination behavior.

Supported query parameters:

- `q`: optional text search.
- `category`: optional category filter.
- `isActive`: optional active/inactive filter.
- `page` and `pageSize`: pagination.

Response: `200 OK`

```json
{
  "items": [
    {
      "id": "uuid",
      "value": "DROP",
      "normalizedValue": "drop",
      "category": "sql",
      "isActive": true,
      "createdAt": "2026-05-09T00:00:00.000Z",
      "updatedAt": "2026-05-09T00:00:00.000Z"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "total": 228
}
```

#### Get Sensitive Word

`GET /api/sensitive-words/{id}`

Swagger documentation should describe the `id` route parameter and `404 Not Found` behavior.

Response: `200 OK`

Returns a single sensitive-word record.

#### Update Sensitive Word

`PUT /api/sensitive-words/{id}`

Swagger documentation should describe the `id` route parameter, request body, validation errors, and missing-record behavior.

Request:

```json
{
  "value": "SELECT",
  "category": "sql",
  "isActive": true
}
```

Response: `200 OK`

Returns the updated sensitive-word record.

#### Delete Sensitive Word

`DELETE /api/sensitive-words/{id}`

Swagger documentation should describe the `id` route parameter and `204 No Content` response.

Response: `204 No Content`

Deletion can be implemented as either a hard delete or a soft delete. If auditability is important, prefer soft delete or `isActive = false`.

### External Business Endpoint

This endpoint is called by the client chat application.

#### Mask Message

`POST /api/messages/mask`

Swagger documentation should describe this as the external business endpoint used by the chat client.

Request:

```json
{
  "message": "SELECT * FROM sensitiveWords"
}
```

Response: `200 OK`

```json
{
  "originalMessage": "SELECT * FROM sensitiveWords",
  "maskedMessage": "****** * **** sensitiveWords",
  "matches": [
    {
      "value": "SELECT",
      "start": 0,
      "end": 6
    },
    {
      "value": "FROM",
      "start": 9,
      "end": 13
    }
  ]
}
```

For a minimal public contract, `matches` can be omitted. Keeping it during development is useful for tests and debugging; if exposed externally, ensure it does not leak sensitive configuration that should remain private.

## 10. MVC Frontend Requirements

The frontend must be a simple ASP.NET Core MVC application.

Important constraint:

- The frontend must not connect directly to MSSQL.
- The frontend should call the REST API for all sensitive-word management and masking behavior.

Required pages:

### Admin Page

The Admin page should allow an internal user to manage sensitive words through the API.

Minimum functionality:

- View sensitive words.
- Search or filter sensitive words.
- Create a sensitive word.
- Edit a sensitive word.
- Delete or deactivate a sensitive word.
- Display validation errors returned by the API.

### Mock Chat Page

The mock Chat page should demonstrate the blooping behavior.

Minimum functionality:

- Provide a message input.
- Submit the message to the API masking endpoint.
- Display the original message and masked message.
- Show a few seed/example messages for quick demonstration.

## 11. Validation And Error Handling

- `message` is required for the masking endpoint.
- Empty messages should return an empty `maskedMessage`.
- Message length should be capped to protect the API, for example 10,000 characters.
- Sensitive word `value` is required, trimmed, and must not be empty.
- Duplicate sensitive words should be rejected using normalized uniqueness.
- Invalid JSON should return `400 Bad Request`.
- Missing records should return `404 Not Found`.
- Validation failures should return clear `400` responses with field-level errors.
- Unexpected failures should return `500 Internal Server Error` without leaking internals.

## 12. Data Preload

On first setup, the service must load entries from:

```text
docs/sql_sensitive_list.txt
```

Preload requirements:

- Import every listed entry.
- Trim quotes, commas, and whitespace from the source format.
- Store entries under category `sql`.
- Normalize values for duplicate detection.
- Make the seed operation idempotent so it can run safely more than once.
- Persist the entries to MSSQL.

## 13. Testing Requirements

Minimum tests:

- CRUD create, list, get, update, delete.
- Preload imports all 228 supplied entries.
- Masking is case-insensitive.
- Masking preserves non-sensitive text.
- Masking respects word boundaries.
- Phrase matching works.
- Longest match wins for overlapping terms.
- Empty message handling.
- Validation errors for invalid inputs.
- MVC pages call the API rather than database services directly.
- Swagger/OpenAPI document includes the expected endpoints.

Useful examples:

```text
SELECT -> ******
drop table users -> **** ***** users
DROPLET -> DROPLET
SELECT * FROM users -> ************* users, if phrase matching wins
```

## 14. Security And Operational Requirements

- Internal CRUD endpoints should require authentication or network-level restriction.
- External masking endpoint should be rate limited.
- Request payload size should be limited.
- Logs should avoid storing raw chat messages unless explicitly required.
- Database credentials and secrets should be provided through environment variables.
- API responses should not expose stack traces.
- The service should expose health/readiness endpoints for deployment checks.
- The MVC frontend should be deployed as a separate web surface or as a separate application area, but it should still consume the API over HTTP.

Recommended support endpoints:

- `GET /healthz`: process is alive.
- `GET /readyz`: process can connect to required dependencies such as the database.

## 15. Performance Enhancements

The project should include a short explanation of what could be done to improve performance.

Recommended performance improvements:

- Cache the active sensitive-word list in memory and refresh it when CRUD changes occur.
- Normalize and precompile matching structures instead of rebuilding regular expressions per request.
- Use longest-match ordering to avoid repeated work and inconsistent overlapping replacements.
- Add database indexes on `normalizedValue`, `category`, and `isActive`.
- Paginate the Admin page and list endpoint instead of loading all records blindly.
- Use async database and HTTP calls throughout the API and MVC frontend.
- Add response compression only if payloads become large enough to justify it.
- Add rate limiting to the public masking endpoint.
- Keep chat message logs minimal to reduce I/O and privacy risk.
- Use load testing to find the real bottleneck before optimizing further.

## 16. Additional Enhancements

The project should include a short explanation of what could be added to make the project more complete.

Useful enhancements:

- Authentication and role-based authorization for Admin CRUD endpoints.
- Audit logging for create, update, delete, activate, and deactivate actions.
- Bulk import/export of sensitive words.
- Category-based masking policies.
- Soft delete and restore for sensitive words.
- Versioned API endpoints.
- Better observability with structured logs, metrics, tracing, and dashboards.
- CI pipeline running build, tests, and static analysis.
- Docker Compose for local API, MVC app, and MSSQL startup.
- Production-ready secret management.
- Frontend polish with confirmation dialogs, inline validation, loading states, and clear error states.
- Optional matching modes, such as exact word, phrase, contains, or regex, if safely controlled.

## 17. Production Deployment Walkthrough

Recommended production shape:

1. Package the API and MVC frontend as deployable .NET applications, preferably container images.
2. Run automated tests and linting in CI on every pull request.
3. Build and publish the images from the main branch.
4. Deploy the API and MVC frontend to a managed container platform such as Azure Container Apps, Azure App Service, AWS ECS/Fargate, Google Cloud Run, or Kubernetes.
5. Use managed MSSQL, such as Azure SQL Database or SQL Server on managed infrastructure, for sensitive-word storage.
6. Run database migrations during deployment using a controlled migration step.
7. Run the preload seed after migrations; it must be idempotent.
8. Store secrets in the platform secret manager, not in source control.
9. Place the API and MVC frontend behind HTTPS using a managed load balancer or platform ingress.
10. Restrict CRUD endpoints to internal callers with authentication, private networking, or both.
11. Add structured logs, metrics, and alerts for error rate, latency, and request volume.
12. Use blue-green, rolling, or canary deployment to reduce release risk.
13. Back up the database and define restore procedures.

## 18. Acceptance Criteria

The project is complete when:

- The database contains the supplied SQL-sensitive entries.
- MSSQL is used as the persistence backend.
- Internal consumers can create, read, update, and delete sensitive words.
- The external masking endpoint returns a correctly masked message.
- Swagger documents all endpoints, parameters, request bodies, responses, and status codes.
- The MVC Admin page manages sensitive words through the API.
- The MVC mock Chat page demonstrates masking through the API.
- The frontend does not connect directly to the database.
- Matching behavior is covered by tests.
- API validation and error handling are predictable.
- The README or deployment notes explain how the service would run in production.
- Performance and additional enhancement notes are included.

## 19. Initial Build Plan

1. Create the .NET solution structure for API, MVC frontend, domain/application code, and tests.
2. Add local MSSQL setup through Docker Compose.
3. Define the sensitive-word data model and migration.
4. Implement the preload script for `docs/sql_sensitive_list.txt`.
5. Implement internal CRUD endpoints in the API.
6. Implement the masking service and external endpoint.
7. Add Swagger/OpenAPI documentation and annotations.
8. Implement the MVC Admin page using API calls.
9. Implement the MVC mock Chat page using the masking API.
10. Add focused automated tests.
11. Add local run instructions, performance notes, additional enhancement notes, and production deployment notes.
