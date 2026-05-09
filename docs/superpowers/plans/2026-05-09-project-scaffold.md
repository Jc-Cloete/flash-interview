# Project Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a compile-ready .NET interview project scaffold with API, MVC frontend, MSSQL development infrastructure, production-style containers, Serilog logging, and accurate project docs.

**Architecture:** The API owns persistence and business behavior; the MVC frontend communicates with the API over HTTP and never references infrastructure or EF Core. Shared application contracts and masking/seeding helpers live in `FlashInterview.Application`, while `FlashInterview.Infrastructure` owns SQL Server access.

**Tech Stack:** .NET 10, ASP.NET Core Web API, ASP.NET Core MVC, EF Core SQL Server, Swashbuckle Swagger annotations, Serilog, xUnit, Docker Compose, MSSQL.

---

### Task 1: Solution And Projects

**Files:**
- Create: `FlashInterview.slnx`
- Create: `src/FlashInterview.Application/`
- Create: `src/FlashInterview.Infrastructure/`
- Create: `src/FlashInterview.Api/`
- Create: `src/FlashInterview.Web/`
- Create: `tests/FlashInterview.Tests/`

- [x] **Step 1: Scaffold solution and project shells**

Run:

```bash
dotnet new sln -n FlashInterview
dotnet new classlib -n FlashInterview.Application -o src/FlashInterview.Application --framework net10.0
dotnet new classlib -n FlashInterview.Infrastructure -o src/FlashInterview.Infrastructure --framework net10.0
dotnet new webapi -n FlashInterview.Api -o src/FlashInterview.Api --framework net10.0 --use-controllers
dotnet new mvc -n FlashInterview.Web -o src/FlashInterview.Web --framework net10.0
dotnet new xunit -n FlashInterview.Tests -o tests/FlashInterview.Tests --framework net10.0
```

Expected: projects are created successfully.

- [x] **Step 2: Add projects and references**

Run:

```bash
dotnet sln FlashInterview.slnx add src/FlashInterview.Application/FlashInterview.Application.csproj
dotnet sln FlashInterview.slnx add src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj
dotnet sln FlashInterview.slnx add src/FlashInterview.Api/FlashInterview.Api.csproj
dotnet sln FlashInterview.slnx add src/FlashInterview.Web/FlashInterview.Web.csproj
dotnet sln FlashInterview.slnx add tests/FlashInterview.Tests/FlashInterview.Tests.csproj
dotnet add src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj reference src/FlashInterview.Application/FlashInterview.Application.csproj
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj reference src/FlashInterview.Application/FlashInterview.Application.csproj src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj reference src/FlashInterview.Application/FlashInterview.Application.csproj
dotnet add tests/FlashInterview.Tests/FlashInterview.Tests.csproj reference src/FlashInterview.Application/FlashInterview.Application.csproj src/FlashInterview.Api/FlashInterview.Api.csproj src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj
```

Expected: all references are added.

### Task 2: Shared Contracts And Basic Tests

**Files:**
- Create: `src/FlashInterview.Application/SensitiveWords/`
- Create: `tests/FlashInterview.Tests/`

- [x] **Step 1: Write seed parser and masking tests first**

Run targeted tests before implementation and confirm they fail because types are missing:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --no-restore
```

Expected: compile failure for missing application types.

- [x] **Step 2: Implement contracts and basic deterministic helpers**

Create DTOs, parser, and masking service in application project.

- [x] **Step 3: Verify tests pass**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj
```

Expected: test pass.

### Task 3: API, MVC, Infrastructure, Logging, And Swagger

**Files:**
- Modify: `src/FlashInterview.Api/Program.cs`
- Create: `src/FlashInterview.Api/Controllers/`
- Modify: `src/FlashInterview.Web/Program.cs`
- Create: `src/FlashInterview.Infrastructure/`
- Modify: project files for packages.

- [x] **Step 1: Add packages**

Add EF Core SQL Server, Serilog, and Swagger packages to the relevant projects.

- [x] **Step 2: Configure API**

Configure controllers, Swagger annotations, Serilog, SQL Server DbContext, repository registration, health checks, and seed service wiring.

- [x] **Step 3: Configure MVC**

Configure Serilog and a typed API client. Keep the frontend database-free.

### Task 4: Docker And Docs

**Files:**
- Create: `docker-compose.dev.yml`
- Create: `docker-compose.yml`
- Create: `src/FlashInterview.Api/Dockerfile`
- Create: `src/FlashInterview.Web/Dockerfile`
- Create: `src/FlashInterview.Api/Dockerfile.dev`
- Create: `src/FlashInterview.Web/Dockerfile.dev`
- Create: `README.md`
- Create: `AGENTS.md`

- [x] **Step 1: Add Docker files**

Create hot-reload development compose and production-style compose with MSSQL.

- [x] **Step 2: Add accurate docs**

Document structure, run commands, logging, database ownership, and agent rules.

- [x] **Step 3: Verify**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Expected: restore, build, and tests pass.
