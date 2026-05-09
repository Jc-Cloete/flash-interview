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
- Keep database bootstrap, migrations, seeding, health, and deployment behavior aligned with `README.md`.
- Keep admin-key flow, masking rate limits/request-size limits, and API surface behavior aligned with `docs/spec.md`.
- Preserve privacy-focused logging: do not log raw chat message bodies unless a future requirement explicitly allows it.
- Avoid adding a direct database dependency to `FlashInterview.Web`.
- Keep PR workflows non-publishing; publishing permissions belong only in release workflows.
- Release assets should include enough deployment metadata to run published GHCR images without rebuilding from source.
- Release tags are validated by `.github/scripts/validate_semver_tag.py`; keep release tags Docker-compatible SemVer and strictly greater than previous SemVer tags.
