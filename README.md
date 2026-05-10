# Flash Interview

Sensitive Words interview project built with C# .NET Core, ASP.NET Core Web API, ASP.NET Core MVC, MSSQL, Swagger/OpenAPI, and Serilog.

The service stores configurable sensitive words in MSSQL and exposes:

- A REST API for internal sensitive-word CRUD protected by an admin API key.
- A rate-limited REST API endpoint for masking/blooping chat messages.
- Local username/password authentication, optional Google sign-in with automatic plain-user provisioning, and user-management APIs owned by the API.
- A simple MVC Admin page that manages words through the API.
- A simple MVC mock Chat page that demonstrates masking through the API.

The MVC frontend must not connect directly to the database. Database and Identity SQL persistence belong to the API/infrastructure layer only. The MVC app talks to the API over HTTP and signs in a local MVC cookie from authenticated user DTOs returned by the API.

## Architecture Guide

### Runtime Components

```mermaid
flowchart LR
    Browser["Browser"]
    Web["FlashInterview.Web<br/>ASP.NET Core MVC<br/>localhost:7002"]
    Api["FlashInterview.Api<br/>ASP.NET Core Web API<br/>localhost:7001"]
    App["FlashInterview.Application<br/>contracts + masking logic"]
    Infra["FlashInterview.Infrastructure<br/>EF Core repositories + Identity store + migrations + seed"]
    Sql["MSSQL<br/>SensitiveWords + ASP.NET Identity tables<br/>localhost:1433"]
    Swagger["Swagger UI<br/>/swagger on API only"]

    Browser -->|"Admin + Mock Chat pages"| Web
    Browser -->|"Swagger UI"| Swagger
    Swagger --> Api
    Web -->|"HTTP API calls + local auth cookie"| Api
    Api --> App
    Api --> Infra
    Infra --> Sql
```

### Code Boundaries

```mermaid
flowchart TB
    WebLayer["FlashInterview.Web<br/>MVC controllers, views, API client"]
    ApiLayer["FlashInterview.Api<br/>REST controllers, Identity auth APIs, rate limits, Swagger"]
    ApplicationLayer["FlashInterview.Application<br/>DTOs, requests, repository interface, masker, seed parser"]
    InfrastructureLayer["FlashInterview.Infrastructure<br/>DbContext, EF repositories, Identity SQL store, migrations, bootstrap, seeder"]
    Database["MSSQL"]

    WebLayer -->|"uses application DTOs/contracts only"| ApplicationLayer
    WebLayer -->|"calls over HTTP"| ApiLayer
    ApiLayer --> ApplicationLayer
    ApiLayer --> InfrastructureLayer
    InfrastructureLayer --> ApplicationLayer
    InfrastructureLayer --> Database

    WebLayer -. "must not reference" .-> InfrastructureLayer
    WebLayer -. "must not connect to" .-> Database
```

Identity and role persistence use ASP.NET Core IdentityCore in `FlashInterview.Infrastructure` through the API host. `FlashInterview.Web` does not reference Infrastructure, EF Core, SQL client packages, `FlashInterviewDbContext`, or sensitive-word persistence implementations; architecture tests enforce that boundary.

### Admin Data Flow

```mermaid
sequenceDiagram
    participant User as Admin user
    participant Web as FlashInterview.Web
    participant Api as FlashInterview.Api
    participant Repo as SqlSensitiveWordRepository
    participant Db as MSSQL

    User->>Web: Open /Admin or submit create/edit/delete/deactivate
    Web->>Api: HTTP request to /api/sensitive-words with X-Admin-Api-Key
    Api->>Api: Validate API key, request model, duplicate rules
    Api->>Repo: Create/List/Get/Update/Delete
    Repo->>Db: EF Core query/command
    Db-->>Repo: Rows affected or result set
    Repo-->>Api: SensitiveWordDto or PagedResponse
    Api-->>Web: JSON response or validation problem
    Web-->>User: Render Admin page with data or validation errors
```

### Local Auth And Identity Flow

```mermaid
sequenceDiagram
    participant User as Admin user
    participant Web as FlashInterview.Web
    participant Api as FlashInterview.Api
    participant Identity as ASP.NET Core IdentityCore
    participant Db as MSSQL

    User->>Web: Submit local login or complete Google callback
    Web->>Api: Internal auth request with X-Admin-Api-Key
    Api->>Identity: Verify password, link existing Google login, or create plain Google user
    Identity->>Db: Read/write Identity users, roles, and logins
    Db-->>Identity: Identity records
    Identity-->>Api: Authenticated user and roles
    Api-->>Web: AuthenticatedUserDto
    Web->>Web: Issue local MVC auth cookie with claims
    Web-->>User: Render protected MVC admin/user-management pages
```

`/api/messages/mask` is intentionally outside the login flow. It remains anonymous for high-throughput chat masking and is protected by request-size limits plus the configured fixed-window rate limit.

### Mock Chat Data Flow

```mermaid
sequenceDiagram
    participant User as Chat user
    participant Web as FlashInterview.Web
    participant Api as FlashInterview.Api
    participant Repo as Repository
    participant Masker as SensitiveWordMasker
    participant Db as MSSQL

    User->>Web: Submit message on Mock Chat
    Web->>Api: POST /api/messages/mask
    Api->>Api: Apply request-size limit and rate limit
    Api->>Repo: List active sensitive-word candidates
    Repo->>Db: SELECT active values
    Db-->>Repo: Active values
    Api->>Masker: Mask message with longest-match deterministic rules
    Masker-->>Api: Original message, masked message, matches
    Api-->>Web: MaskMessageResponse
    Web-->>User: Display original and masked message
```

### Startup And Seed Flow

```mermaid
sequenceDiagram
    participant Compose as Docker Compose
    participant Api as API container
    participant Bootstrap as DatabaseBootstrapper
    participant Ef as EF Core
    participant Seeder as SensitiveWordSeeder
    participant Db as MSSQL
    participant File as docs/sql_sensitive_list.txt

    Compose->>Api: Start with environment settings
    Api->>Bootstrap: Hosted service StartAsync
    alt Database__ApplyMigrationsOnStartup=true
        Bootstrap->>Ef: Database.MigrateAsync()
        Ef->>Db: Apply pending migrations
    end
    alt Database__SeedOnStartup=true
        Bootstrap->>Seeder: SeedFromFileAsync(seed file)
        Seeder->>File: Parse 228 preload entries
        Seeder->>Db: Insert missing normalized values only
    end
    Api-->>Compose: API starts listening
```

### CI And Release Flow

```mermaid
flowchart LR
    PR["Pull request / push to main"]
    Checks["PR Checks workflow<br/>restore + build + tests + Docker build checks"]
    Release["GitHub Release or manual dispatch"]
    SemVer["SemVer tag validator<br/>must be greater than prior SemVer tag"]
    Publish["Release Containers workflow<br/>build + test + push images"]
    Ghcr["GHCR API/Web images"]
    Assets["GitHub Release assets<br/>image archives + deployment bundle + checksums"]

    PR --> Checks
    Release --> SemVer --> Publish
    Publish --> Ghcr
    Publish --> Assets
```

## Project Layout

```text
FlashInterview.slnx
.github/
  scripts/
    validate_semver_tag.py          Release tag validation used by CI
  workflows/
    pr-checks.yml                   PR/push restore, build, test, Docker build checks
    release-containers.yml          Release image publishing and asset upload
deploy/
  docker-compose.release.yml        Compose template using published image references
  release.env.example               Release environment variable template
docker-compose.dev.yml              Hot-reload local API, Web, and MSSQL stack
docker-compose.observability.yml    Optional Aspire Dashboard telemetry overlay
docker-compose.yml                  Production-shaped local Compose smoke test
scripts/
  run-local-smoke.sh                 Evaluator-friendly local smoke test helper
src/
  FlashInterview.Application/       Shared contracts, interfaces, seed parsing, masking logic
    SensitiveWords/
      SensitiveWordMasker.cs        Deterministic longest-match masking behavior
      SensitiveWordSeedParser.cs    Parser for the supplied SQL-sensitive preload file
      *Request.cs / *Response.cs    API and MVC shared contracts
  FlashInterview.Infrastructure/    EF Core SQL Server persistence and Identity store
    Auth/                           Identity user and initial super-admin bootstrap
    FlashInterviewDbContext.cs      DbContext for sensitive words and Identity tables
    Migrations/                     Initial MSSQL migration and model snapshot
    DatabaseBootstrapper.cs         Optional startup migrations and idempotent seed
    SensitiveWords/                 Entity, repository, and seed importer
  FlashInterview.Api/               REST API, Swagger, Serilog, auth, rate limits, health
    Controllers/                    Sensitive-word, message masking, auth, and user endpoints
    OpenApi/                        Swagger operation/document filters
    Security/                       Admin API-key authentication handler
  FlashInterview.Web/               ASP.NET Core MVC frontend using API HttpClient
    Clients/                        Typed API clients with internal API-key header behavior
    Controllers/                    Account, Admin, Users, and mock Chat MVC controllers
    Models/                         MVC view models
    Views/                          Razor views
tests/
  FlashInterview.Tests/             xUnit tests
    ApiSurfaceTests.cs              API auth, validation, rate limit, Swagger surface
    AdminWebTests.cs                MVC Admin client/controller/view behavior
    MssqlApiIntegrationTests.cs     Optional MSSQL-backed CRUD/migration/seed coverage
    SensitiveWord*Tests.cs          Masking, normalization, and seed parser tests
    WebProjectArchitectureTests.cs  Frontend database-boundary guard
  FlashInterview.PerformanceTests/  Opt-in BenchmarkDotNet and NBomber performance lab
    MaskerBenchmarks.cs             In-process masking microbenchmarks
    LoadTests/                      API load profiles and report generation
docs/
  spec.md                           Product and delivery specification
  performance-capacity-report-2026-05-09.md Baseline local performance report
  sql_sensitive_list.txt            SQL-sensitive preload list
```

## Requirements

- .NET SDK 10.0+
- Docker Desktop if using Compose
- MSSQL, provided by Compose for local development

## Local Development

Restore, build, and test:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

The test suite includes MSSQL-backed integration tests for the API repository path, migrations, and the 228-entry preload. These tests create and drop isolated `FlashInterviewTests_<guid>` databases and are skipped automatically when SQL Server is not reachable on the default local development connection. To point them at a different SQL Server master database, set `FLASHINTERVIEW_TEST_MSSQL_MASTER`.

Run everything with hot reload:

```bash
docker compose -f docker-compose.dev.yml up --build
```

Codex-local helpers wrap the same development Compose file and load optional local `.env` values for initial super-admin and Google OAuth configuration:

```bash
./scripts/run-codex-dev.sh
./scripts/cleanup-codex-dev.sh
```

`cleanup-codex-dev.sh` preserves named volumes by default, including MSSQL data and ASP.NET Core Data Protection keys. Use `./scripts/cleanup-codex-dev.sh --volumes` only when you want a full local reset; that also invalidates existing MVC auth and antiforgery cookies.

## Performance Lab

The performance lab is opt-in. It combines live observability with repeatable load reports:

- Aspire Dashboard receives OpenTelemetry metrics, traces, and structured application logs from the API and MVC web app.
- API traces include ASP.NET request spans and EF Core database spans so database timing is visible.
- API telemetry includes request rates, request duration, runtime counters, HTTP client metrics, structured request logs, and masking-specific counters/histograms.
- BenchmarkDotNet measures the in-process masking engine without HTTP or SQL Server noise.
- NBomber drives HTTP load against the running API and writes throughput, latency, percentile, success-rate, and saturation reports.

Start the development stack with observability:

```bash
docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build
```

Open the dashboard:

- Aspire Dashboard: `http://localhost:18888`
- API: `http://localhost:7001`
- Web: `http://localhost:7002`

The observability Compose overlay binds dashboard ports to `127.0.0.1` and raises the mask rate limit for local performance-lab runs so capacity profiles measure API behavior rather than the default per-client throttle.

Dependency note: API EF Core span capture currently uses `OpenTelemetry.Instrumentation.EntityFrameworkCore` version `1.15.1-beta.1`, which is prerelease. Keep this visible during dependency reviews and upgrade to the first suitable stable package release when it becomes available.

Run masking microbenchmarks:

```bash
dotnet run -c Release --project tests/FlashInterview.PerformanceTests -- benchmark
```

Run the API load smoke test:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --smoke
```

Run the baseline local load profile:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --profile baseline
```

Run the capacity ramp profile:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --profile capacity
```

Use NBomber reports for high-level throughput, request latency, percentiles, success rate, and saturation points. Use the Aspire Dashboard during the same run to inspect structured logs, API request metrics, trace waterfalls, MVC-to-API calls, EF Core database spans, runtime counters, and masking-specific histograms.

Reports are written to `artifacts/performance/`. BenchmarkDotNet writes to `BenchmarkDotNet.Artifacts/`. Both folders are local artifacts and are not committed.

Development URLs:

- API: `http://localhost:7001`
- Swagger UI: `http://localhost:7001/swagger`
- MVC frontend: `http://localhost:7002`
- MSSQL: `localhost,1433`

The development API container runs `dotnet watch`, applies EF Core migrations on startup, and seeds `/workspace/docs/sql_sensitive_list.txt` when `Database__ApplyMigrationsOnStartup=true` and `Database__SeedOnStartup=true`.
The Compose file mounts project `bin/` and `obj/` directories to named Docker volumes so container restore/build metadata does not overwrite host-side .NET build metadata. It also persists ASP.NET Core Data Protection keys in named volumes so hot-reload/container restarts do not invalidate MVC auth and antiforgery cookies.
Development Compose sets a local-only admin API key for API/Web communication. Initial super-admin bootstrap and Google OAuth are disabled unless the related environment variables are supplied.

On Apple Silicon, the MSSQL container uses `platform: linux/amd64`, so Docker runs it through emulation.

## Production-Style Compose

Run the production-style containers:

```bash
docker compose up --build
```

For an evaluator-friendly local smoke test that builds images, starts MSSQL, applies migrations, seeds the supplied sensitive-word list, and waits for API readiness:

```bash
./scripts/run-local-smoke.sh
```

This command uses existing shell variables first, then values from `.env`, then local development defaults for `MSSQL_SA_PASSWORD` and `FLASHINTERVIEW_ADMIN_API_KEY`. It leaves the stack running on success so the API and MVC frontend can be inspected; stop it with `docker compose down`. On timeout or interruption, it prints recent API logs and stops the stack.

Production-style URLs:

- API: `http://localhost:8080`
- MVC frontend: `http://localhost:8081`

The production Compose file does not bind-mount source code and does not enable hot reload. It is intended as a deployment-shaped local smoke test, not a substitute for managed production infrastructure.
Automatic migrations and seed preload default to off in production-style Compose. For controlled deployment, run a one-off API container or release step with `Database__ApplyMigrationsOnStartup=true`; set `Database__SeedOnStartup=true` in the same controlled step only when the preload should be applied. The seed import is idempotent and runs after migrations.

Set `MSSQL_SA_PASSWORD` before running in any shared environment:

```bash
export MSSQL_SA_PASSWORD='replace-with-a-real-secret'
export FLASHINTERVIEW_ADMIN_API_KEY='replace-with-a-real-admin-key'
export INITIAL_SUPER_ADMIN_ENABLED='true'
export INITIAL_SUPER_ADMIN_EMAIL='admin@example.com'
export INITIAL_SUPER_ADMIN_PASSWORD='replace-with-a-real-bootstrap-password'
docker compose up --build
```

## Release Deployment Bundle

Published GitHub Releases include:

- GHCR images for the API and MVC frontend.
- Compressed Docker image archives as release assets for offline inspection or loading with `docker load`.
- A deployment bundle containing `docker-compose.yml`, `.env.example`, `.env.pinned`, image digests, and checksums.

To run a release bundle:

```bash
tar -xzf flash-interview-<tag>-deployment-bundle.tar.gz
cp .env.example .env
# edit .env and set MSSQL_SA_PASSWORD plus the published image tags or pinned digests
docker compose --env-file .env -f docker-compose.yml up -d
```

The checked-in release compose template is `deploy/docker-compose.release.yml`. It uses published image references and does not build from local source.

## CI/CD

GitHub Actions workflows live in `.github/workflows/`:

- `pr-checks.yml` runs on pull requests to `main` and pushes to `main`. It restores, builds, and tests the .NET solution, then verifies both production Dockerfiles build without publishing images.
- `release-containers.yml` runs when a GitHub Release is published or manually through `workflow_dispatch`. It restores/builds/tests, publishes API and Web images to GitHub Container Registry, creates compressed Docker image archives, and uploads release deployment assets.

Release tags must be Docker-compatible SemVer: `vMAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as `v1.2.3-rc.1`. Build metadata such as `+build.1` is rejected because `+` is not valid in Docker image tags. The release workflow fails before publishing anything unless the release tag is greater than every previous SemVer tag in the repository.

Validate a tag locally:

```bash
python3 .github/scripts/validate_semver_tag.py v1.0.0
```

## Configuration

API configuration:

- `ConnectionStrings__DefaultConnection`: MSSQL connection string.
- `Database__ApplyMigrationsOnStartup`: set to `true` only for a controlled migration step.
- `Database__SeedOnStartup`: set to `true` to run the idempotent preload after the migration step.
- `Database__SeedFile`: path to the preload file.
- `Security__AdminApiKey`: required API key for internal sensitive-word CRUD. If missing, protected endpoints fail closed.
- `Security__InitialSuperAdmin__Enabled`: set to `true` in a controlled bootstrap step to create/update the initial super-admin account.
- `Security__InitialSuperAdmin__Email`: initial super-admin email address.
- `Security__InitialSuperAdmin__Password`: initial super-admin password. Supply through environment variables or secrets management, not source control.
- `Security__MaskRateLimit__PermitLimit`: fixed-window permit count for `POST /api/messages/mask`, default `60`.
- `Security__MaskRateLimit__WindowSeconds`: fixed-window length in seconds for `POST /api/messages/mask`, default `60`.
- `OpenTelemetry__ServiceName`: service name shown in the Aspire Dashboard.
- `OpenTelemetry__OtlpEndpoint`: OTLP endpoint for logs, metrics, and traces. Leave unset to disable export.

MVC configuration:

- `SensitiveWordsApi__BaseUrl`: base URL for the REST API.
- `SensitiveWordsApi__AdminApiKey`: API key sent only on MVC-to-API internal requests such as Admin CRUD, local login, external sign-in, and user management. Production deployments must supply this through environment variables or secrets management; the checked-in production appsettings value is only a placeholder.
- `Authentication__Google__ClientId`: optional Google OAuth client id. Leave blank to hide/disable Google sign-in.
- `Authentication__Google__ClientSecret`: optional Google OAuth client secret. Leave blank to hide/disable Google sign-in.
- `DataProtection__KeysPath`: optional persistent ASP.NET Core Data Protection key-ring path for MVC auth cookies and OAuth state. The release Compose template mounts this at `/root/.aspnet/DataProtection-Keys`.

API/Infrastructure own password verification, Identity SQL persistence, Google-login linking/provisioning, role assignment, and user-management APIs. Verified Google emails can create plain non-admin users automatically; admins can promote users through user management. The MVC frontend uses HTTP plus its own local cookie; it must not reference EF Core, SQL clients, Infrastructure, `FlashInterviewDbContext`, or sensitive-word persistence implementations.

## Logging

Serilog is configured in both web applications:

- `FlashInterview.Api`
- `FlashInterview.Web`

Both write structured logs to console and use Serilog request logging for HTTP requests. Container platforms can collect logs directly from stdout/stderr.

When `OpenTelemetry__OtlpEndpoint` is configured, application logs are also exported to the Aspire Dashboard through the Microsoft OpenTelemetry logging provider. Request logs are enriched with validated `CorrelationId`, `SessionId`, and `TraceIdentifier` fields. The API and MVC apps accept optional `X-Correlation-Id` and `X-Session-Id` headers, echo the sanitized values in responses, and fall back to generated identifiers when submitted values are blank, longer than 64 characters, or contain characters other than letters, digits, `-`, and `_`.

Request log events include the application identity, HTTP method, path, status code, and elapsed time. Development and container-development settings keep framework, EF Core SQL command, and outbound HttpClient logs at `Warning` to avoid drowning out useful request and application events.

Unexpected exceptions are handled globally in both web entrypoints. The exception is logged server-side with request method/path context, while API clients receive a generic problem response and MVC users receive a generic error page without stack traces. Do not add request-body logging for `POST /api/messages/mask` or MVC chat submissions; raw chat message bodies are intentionally excluded from logs.

## API Surface

Sensitive-word CRUD:

- `POST /api/sensitive-words`
- `GET /api/sensitive-words`
- `GET /api/sensitive-words/{id}`
- `PUT /api/sensitive-words/{id}`
- `DELETE /api/sensitive-words/{id}`

These internal endpoints require the `X-Admin-Api-Key` header.

Internal auth and user-management APIs:

- `POST /api/auth/login`
- `POST /api/auth/external-login/sign-in`
- User-management endpoints under `/api/users`

These endpoints are for the MVC frontend and require the `X-Admin-Api-Key` header. Identity state is stored through the API/Infrastructure MSSQL path, not by the MVC project.

Masking endpoint:

- `POST /api/messages/mask`

The masking endpoint does not require the admin API key. It is rate limited and rejects oversized request bodies before normal message validation.

Health endpoints:

- `GET /healthz`: process liveness only; it does not require MSSQL.
- `GET /readyz`: readiness check that verifies the API can reach MSSQL.

Swagger UI is enabled in development on the API app at `http://localhost:7001/swagger`. The MVC frontend on `http://localhost:7002` does not host Swagger.
Swagger UI and JSON endpoints are intentionally enabled only for Development in this submission. The OpenAPI generation is covered by tests; exposing Swagger in production would require an explicit code/configuration change and should be placed behind authenticated internal access if the hosting environment requires live API documentation.

## Test Coverage

The current xUnit suite covers:

- Sensitive-word normalization, seed parsing, and deterministic masking edge cases.
- REST API surface behavior, admin API-key protection, and mask rate limiting using `WebApplicationFactory` with a fake repository, so endpoint checks do not require MSSQL.
- Identity bootstrap, internal auth APIs, MVC cookie login/logout, optional Google configuration, and user-management authorization.
- Optional MSSQL-backed API and preload integration coverage using isolated test databases when local SQL Server is available.
- CI starts a SQL Server service container so MSSQL-backed CRUD, migration, and seed tests run in pull-request checks instead of being silently skipped.
- MVC project architecture guards that prevent direct EF Core, SQL Server, or infrastructure references in the frontend.
- MVC API-client behavior, including sending the admin API key only for Admin requests.
- Performance lab command parsing and API correlation/session header behavior.

## Current Codebase Status

The scaffold is compile-ready and includes core deterministic masking behavior, REST API surface tests, admin API-key protection for internal CRUD and auth/user-management APIs, rate limiting for the mask endpoint, completed basic Admin management workflows, frontend database-boundary checks, and EF Core migrations for the MSSQL schema. Database bootstrap uses controlled `MigrateAsync` startup behavior when explicitly enabled; production-style Compose leaves it disabled by default. The mask endpoint uses a cached compiled matcher that is invalidated after sensitive-word writes, so normal chat requests do not rebuild the active word list on every call.
