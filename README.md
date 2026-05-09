# Flash Interview

Sensitive Words interview project built with C# .NET Core, ASP.NET Core Web API, ASP.NET Core MVC, MSSQL, Swagger/OpenAPI, and Serilog.

The service stores configurable sensitive words in MSSQL and exposes:

- A REST API for internal sensitive-word CRUD.
- A REST API endpoint for masking/blooping chat messages.
- A simple MVC Admin page that manages words through the API.
- A simple MVC mock Chat page that demonstrates masking through the API.

The MVC frontend must not connect directly to the database. Database access belongs to the API/infrastructure layer only.

## Project Layout

```text
FlashInterview.slnx
src/
  FlashInterview.Application/      Shared contracts, interfaces, seed parsing, masking logic
  FlashInterview.Infrastructure/   EF Core SQL Server DbContext, repository, seed bootstrap
  FlashInterview.Api/              REST API, Swagger, Serilog, health checks
  FlashInterview.Web/              ASP.NET Core MVC frontend using API HttpClient
tests/
  FlashInterview.Tests/            xUnit tests
docs/
  spec.md                          Product and delivery specification
  sql_sensitive_list.txt           SQL-sensitive preload list
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

Run everything with hot reload:

```bash
docker compose -f docker-compose.dev.yml up --build
```

Development URLs:

- API: `http://localhost:7001`
- Swagger UI: `http://localhost:7001/swagger`
- MVC frontend: `http://localhost:7002`
- MSSQL: `localhost,1433`

The development API container runs `dotnet watch`, applies EF Core migrations on startup, and seeds `/workspace/docs/sql_sensitive_list.txt` when `Database__ApplyMigrationsOnStartup=true` and `Database__SeedOnStartup=true`.
The Compose file mounts project `bin/` and `obj/` directories to named Docker volumes so container restore/build metadata does not overwrite host-side .NET build metadata.

On Apple Silicon, the MSSQL container uses `platform: linux/amd64`, so Docker runs it through emulation.

## Production-Style Compose

Run the production-style containers:

```bash
docker compose up --build
```

Production-style URLs:

- API: `http://localhost:8080`
- MVC frontend: `http://localhost:8081`

The production Compose file does not bind-mount source code and does not enable hot reload. It is intended as a deployment-shaped local smoke test, not a substitute for managed production infrastructure.
Automatic migrations and seed preload default to off in production-style Compose. For controlled deployment, run a one-off API container or release step with `DATABASE_APPLY_MIGRATIONS_ON_STARTUP=true`; set `DATABASE_SEED_ON_STARTUP=true` in the same controlled step only when the preload should be applied. The seed import is idempotent and runs after migrations.

Set `MSSQL_SA_PASSWORD` before running in any shared environment:

```bash
export MSSQL_SA_PASSWORD='replace-with-a-real-secret'
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

MVC configuration:

- `SensitiveWordsApi__BaseUrl`: base URL for the REST API.

## Logging

Serilog is configured in both web applications:

- `FlashInterview.Api`
- `FlashInterview.Web`

Both write structured logs to console and use Serilog request logging for HTTP requests. Container platforms can collect logs directly from stdout/stderr.

Request log events include the application identity, HTTP method, path, status code, and elapsed time. Development and container-development settings keep framework, EF Core SQL command, and outbound HttpClient logs at `Warning` to avoid drowning out useful request and application events.

Unexpected exceptions are handled globally in both web entrypoints. The exception is logged server-side with request method/path context, while API clients receive a generic problem response and MVC users receive a generic error page without stack traces. Do not add request-body logging for `POST /api/messages/mask` or MVC chat submissions; raw chat message bodies are intentionally excluded from logs.

## API Surface

Sensitive-word CRUD:

- `POST /api/sensitive-words`
- `GET /api/sensitive-words`
- `GET /api/sensitive-words/{id}`
- `PUT /api/sensitive-words/{id}`
- `DELETE /api/sensitive-words/{id}`

Masking endpoint:

- `POST /api/messages/mask`

Health endpoints:

- `GET /healthz`
- `GET /readyz`

Swagger UI is enabled in development at `/swagger`.

## Test Coverage

The current xUnit suite covers:

- Sensitive-word normalization, seed parsing, and deterministic masking edge cases.
- REST API surface behavior using `WebApplicationFactory` with a fake repository, so endpoint checks do not require MSSQL.
- MVC project architecture guards that prevent direct EF Core, SQL Server, or infrastructure references in the frontend.

## Current Scaffold Status

The scaffold is compile-ready and includes core deterministic masking behavior, REST API surface tests, completed basic Admin management workflows, frontend database-boundary checks, and an initial EF Core migration for the MSSQL schema. Database bootstrap uses controlled `MigrateAsync` startup behavior when explicitly enabled; production-style Compose leaves it disabled by default.

## Next Implementation Steps

1. Add database-backed integration tests around the API and MSSQL.
2. Add authentication/authorization for admin endpoints.
3. Add rate limiting for the masking endpoint.
4. Expand Swagger examples and response documentation.
