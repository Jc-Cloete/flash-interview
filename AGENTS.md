# Agent Guide

This file is the project-scoped memory for Codex work in this repository. Keep it short and keep detailed behavior in `docs/`.

## Read First

1. `README.md` - current architecture, commands, Docker usage, and scaffold status.
2. `docs/spec.md` - source-of-truth product requirements from the PDF and email.
3. `docs/superpowers/plans/` - active implementation plans.

## Hard Constraints

- Use C# .NET Core / ASP.NET Core for this interview project.
- Use MSSQL as the database backend.
- Use Serilog for system-wide logging in web entrypoints.
- Keep Swagger/OpenAPI documentation accurate for API endpoints.
- The MVC frontend must not connect to MSSQL or reference EF Core infrastructure.
- The API/infrastructure layer owns all database access.
- Keep `README.md` accurate when commands, ports, structure, or architecture change.
- Keep this file as a map, not a long manual.

## Current Structure

```text
src/FlashInterview.Application      Contracts, interfaces, seed parsing, masking
src/FlashInterview.Infrastructure   EF Core SQL Server persistence
src/FlashInterview.Api              REST API, Swagger, Serilog, health
src/FlashInterview.Web              MVC frontend using API HttpClient
tests/FlashInterview.Tests          xUnit tests
.github/workflows                   PR checks and release container publishing
deploy                              Release compose template and env example
```

## Verification Commands

Run these before claiming scaffold or behavior is complete:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Use Docker hot reload for local end-to-end manual checks:

```bash
docker compose -f docker-compose.dev.yml up --build
```

## Implementation Notes

- Prefer focused tests for application behavior before implementing business logic.
- Keep masking behavior deterministic and testable in `FlashInterview.Application`.
- Keep API surface tests isolated from MSSQL by replacing persistence with a fake repository unless the test is intentionally database-backed.
- Keep MVC architecture tests guarding against direct database/infrastructure dependencies in `FlashInterview.Web`.
- Use EF Core migrations for database bootstrap; do not add new `EnsureCreated` startup paths.
- Keep startup migrations and seed preload behind separate opt-in flags, with seeding after migrations.
- Do not log raw chat message bodies unless a future requirement explicitly allows it.
- Keep Serilog request logs structured with application identity, method, path, status, and elapsed time; keep EF Core command noise at `Warning` in development/container settings.
- Preserve global exception handlers that log unexpected failures server-side without returning stack traces to API or MVC clients.
- Keep sensitive-word CRUD protected by `X-Admin-Api-Key` through `Security:AdminApiKey`; missing configuration must fail closed.
- Keep the MVC Admin API client sending `SensitiveWordsApi:AdminApiKey` only to internal CRUD/list calls, not the public mask call.
- Keep `POST /api/messages/mask` on the configurable fixed-window `Security:MaskRateLimit` policy and request-size limited.
- Avoid adding a direct database dependency to `FlashInterview.Web`.
- Keep PR workflows non-publishing; publishing permissions belong only in release workflows.
- Release assets should include enough deployment metadata to run published GHCR images without rebuilding from source.
- Release tags are validated by `.github/scripts/validate_semver_tag.py`; keep release tags Docker-compatible SemVer and strictly greater than previous SemVer tags.
