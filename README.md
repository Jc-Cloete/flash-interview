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

The development API container runs `dotnet watch`, creates the database on startup, and seeds `/workspace/docs/sql_sensitive_list.txt` when `Database__EnsureCreatedOnStartup=true`.
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

Set `MSSQL_SA_PASSWORD` before running in any shared environment:

```bash
export MSSQL_SA_PASSWORD='replace-with-a-real-secret'
docker compose up --build
```

## Configuration

API configuration:

- `ConnectionStrings__DefaultConnection`: MSSQL connection string.
- `Database__EnsureCreatedOnStartup`: set to `true` for development-only automatic schema creation.
- `Database__SeedFile`: path to the preload file.

MVC configuration:

- `SensitiveWordsApi__BaseUrl`: base URL for the REST API.

## Logging

Serilog is configured in both web applications:

- `FlashInterview.Api`
- `FlashInterview.Web`

Both write structured logs to console and use Serilog request logging for HTTP requests. Container platforms can collect logs directly from stdout/stderr.

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

## Current Scaffold Status

The scaffold is compile-ready and includes core deterministic masking behavior plus seed parsing tests. The current database bootstrap uses `EnsureCreated` for development convenience. A later implementation pass should replace this with explicit EF Core migrations before treating the production Compose file as release-grade.

## Next Implementation Steps

1. Add EF Core migrations for MSSQL.
2. Add integration tests around the API and database.
3. Complete admin edit/deactivate behavior in the MVC frontend.
4. Add authentication/authorization for admin endpoints.
5. Expand Swagger examples and response documentation.
