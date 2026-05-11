# Architecture Smell Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the architecture smells found in review while preserving the ASP.NET Core, MSSQL, Serilog, Swagger, and MVC-over-HTTP architecture.

**Architecture:** Keep `FlashInterview.Web` as the MVC/UI composition root, delegating auth and user-management work through Application contracts while keeping Identity/EF details in Infrastructure. The Web project remains database-free, sensitive-word cache invalidation is centralized behind an API service, and shared hosting setup lives in the real `FlashInterview.Hosting` project.

**Tech Stack:** C#/.NET 10, ASP.NET Core MVC/Web API, ASP.NET Core Identity, EF Core SQL Server, xUnit, Serilog, OpenTelemetry, Swagger/Swashbuckle.

---

## Current File Structure

- `src/FlashInterview.Application/Auth/IAuthWorkflow.cs`: Application-facing local/external auth workflow contract and typed result records.
- `src/FlashInterview.Application/Auth/IUserManagementWorkflow.cs`: Application-facing user-management workflow contract and typed result records.
- `src/FlashInterview.Infrastructure/Auth/AuthWorkflow.cs`: Identity-backed local and external sign-in orchestration.
- `src/FlashInterview.Infrastructure/Auth/UserManagementWorkflow.cs`: Identity/EF-backed user listing, local-user creation, and admin-role orchestration.
- `src/FlashInterview.Infrastructure/Auth/IdentityResultExtensions.cs`: Identity error mapping for Application workflow validation results.
- `src/FlashInterview.Application/SensitiveWords/ISensitiveWordRepository.cs`: sensitive-word persistence contract.
- `src/FlashInterview.Infrastructure/SensitiveWords/SqlSensitiveWordRepository.cs`: SQL Server implementation of the sensitive-word repository.
- `src/FlashInterview.Api/SensitiveWords/ISensitiveWordAdministrationService.cs`: API service boundary for sensitive-word administration and matcher-cache invalidation.
- `src/FlashInterview.Api/SensitiveWords/SensitiveWordAdministrationService.cs`: repository delegation plus matcher-cache invalidation after successful create/update/delete.
- `src/FlashInterview.Hosting/FlashInterview.Hosting.csproj`: shared hosting/observability project referenced by API and Web.
- `src/FlashInterview.Hosting/ObservabilityExtensions.cs`: shared Serilog and OpenTelemetry registration helpers.
- `src/FlashInterview.Hosting/CorrelationExtensions.cs`: shared correlation/session middleware and request logging helpers.
- `src/FlashInterview.Api/Controllers/AuthController.cs`: HTTP adapter for auth request parsing, validation, and workflow-result mapping.
- `src/FlashInterview.Api/Controllers/UsersController.cs`: HTTP adapter for user-management request validation and workflow-result mapping.
- `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`: HTTP adapter for sensitive-word administration.
- `src/FlashInterview.Api/Program.cs`: API composition root; registers Infrastructure, API-only services, Swagger, auth, health, and rate limiting.
- `src/FlashInterview.Web/Program.cs`: MVC composition root; references Application and Hosting only.
- `tests/FlashInterview.Tests/WebProjectArchitectureTests.cs`: guards Web project references, source usage, and resolved package closure against database/infrastructure drift.

---

### Task 1: Move Auth Workflow Behind Application Contracts

**Files:**
- Create: `src/FlashInterview.Application/Auth/IAuthWorkflow.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/AuthWorkflow.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/IdentityResultExtensions.cs`
- Modify: `src/FlashInterview.Api/Controllers/AuthController.cs`
- Modify: `src/FlashInterview.Infrastructure/DependencyInjection.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`

- [x] **Step 1: Define Application workflow contract**

`IAuthWorkflow` returns typed `AuthWorkflowResult` values from `FlashInterview.Application.Auth` and does not expose MVC or Infrastructure types.

- [x] **Step 2: Implement Identity-backed workflow in Infrastructure**

`AuthWorkflow` owns `UserManager<FlashInterviewUser>`, `SignInManager<FlashInterviewUser>`, local password checks, external-login linking, user creation, email confirmation, display-name updates, and DTO mapping.

- [x] **Step 3: Keep `AuthController` as an HTTP adapter**

`AuthController` keeps route/Swagger attributes, request parsing, request validation, and maps workflow results to `Ok`, `Unauthorized`, and `ValidationProblem`.

- [x] **Step 4: Register through Infrastructure DI**

`AddFlashInterviewInfrastructure` registers `IAuthWorkflow` to `AuthWorkflow` alongside the Identity/EF setup it depends on.

- [x] **Step 5: Verify**

Run:

```bash
dotnet test FlashInterview.slnx --filter AuthApiTests
dotnet test FlashInterview.slnx
```

---

### Task 2: Move User Management Workflow Behind Application Contracts

**Files:**
- Create: `src/FlashInterview.Application/Auth/IUserManagementWorkflow.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/UserManagementWorkflow.cs`
- Modify: `src/FlashInterview.Api/Controllers/UsersController.cs`
- Modify: `src/FlashInterview.Infrastructure/DependencyInjection.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`

- [x] **Step 1: Define Application workflow contract**

`IUserManagementWorkflow` returns typed user-management results from `FlashInterview.Application.Auth` and does not expose MVC, EF, or Infrastructure types.

- [x] **Step 2: Implement Identity/EF orchestration in Infrastructure**

`UserManagementWorkflow` owns user queries, role joins, local-user creation, admin-role assignment/removal, role creation, security-stamp updates, locked-out calculation, and ordered role projection.

- [x] **Step 3: Keep `UsersController` as an HTTP adapter**

`UsersController` keeps route/Swagger attributes, request validation, and maps workflow results to `Ok`, `Created`, `Conflict`, `NotFound`, and `ValidationProblem`.

- [x] **Step 4: Register through Infrastructure DI**

`AddFlashInterviewInfrastructure` registers `IUserManagementWorkflow` to `UserManagementWorkflow`.

- [x] **Step 5: Verify**

Run:

```bash
dotnet test FlashInterview.slnx --filter AuthApiTests
dotnet test FlashInterview.slnx
```

---

### Task 3: Centralize Sensitive-Word Cache Invalidation

**Files:**
- Use: `src/FlashInterview.Application/SensitiveWords/ISensitiveWordRepository.cs`
- Use: `src/FlashInterview.Infrastructure/SensitiveWords/SqlSensitiveWordRepository.cs`
- Create: `src/FlashInterview.Api/SensitiveWords/ISensitiveWordAdministrationService.cs`
- Create: `src/FlashInterview.Api/SensitiveWords/SensitiveWordAdministrationService.cs`
- Modify: `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [x] **Step 1: Add API administration service**

`ISensitiveWordAdministrationService` exposes create/list/get/update/delete operations using Application request/response DTOs.

- [x] **Step 2: Implement repository delegation plus invalidation**

`SensitiveWordAdministrationService` injects `ISensitiveWordRepository` and `ISensitiveWordMatcherCache`, invalidating after successful create, non-null update, and successful delete.

- [x] **Step 3: Slim `SensitiveWordsController`**

`SensitiveWordsController` depends on the administration service and keeps duplicate-word handling, route/Swagger attributes, and HTTP response mapping.

- [x] **Step 4: Register the service**

`Program.cs` registers `ISensitiveWordAdministrationService` to `SensitiveWordAdministrationService`.

- [x] **Step 5: Verify**

Run:

```bash
dotnet test FlashInterview.slnx --filter ApiSurfaceTests
dotnet test FlashInterview.slnx
```

---

### Task 4: Extract Shared Hosting, Observability, And Correlation Setup

**Files:**
- Create: `src/FlashInterview.Hosting/FlashInterview.Hosting.csproj`
- Create: `src/FlashInterview.Hosting/ObservabilityExtensions.cs`
- Create: `src/FlashInterview.Hosting/CorrelationExtensions.cs`
- Modify: `src/FlashInterview.Api/FlashInterview.Api.csproj`
- Modify: `src/FlashInterview.Web/FlashInterview.Web.csproj`
- Modify: `src/FlashInterview.Api/Program.cs`
- Modify: `src/FlashInterview.Web/Program.cs`
- Modify: `src/FlashInterview.Api/Dockerfile`
- Modify: `src/FlashInterview.Web/Dockerfile`
- Modify: `FlashInterview.slnx`
- Test: `tests/FlashInterview.Tests/WebProjectArchitectureTests.cs`

- [x] **Step 1: Create a real shared Hosting project**

`FlashInterview.Hosting` is a normal class library project referenced by API and Web. API/Web no longer compile linked source files from a hidden shared module.

- [x] **Step 2: Extract shared observability registration**

`ObservabilityExtensions` centralizes Serilog setup, OpenTelemetry logging, metrics, tracing, and optional OTLP exporter behavior while preserving API-specific `MaskingMetrics` and EF tracing configuration.

- [x] **Step 3: Extract correlation and request logging middleware**

`CorrelationExtensions` centralizes `X-Correlation-Id` / `X-Session-Id` validation, response echoing, logger scope enrichment, `LogContext` properties, and Serilog request logging.

- [x] **Step 4: Update entrypoints and Docker restore inputs**

API and Web `Program.cs` call the Hosting extensions. Production Dockerfiles copy `FlashInterview.Hosting.csproj` before restore so `--no-restore` publish paths work.

- [x] **Step 5: Guard the Web boundary**

`WebProjectArchitectureTests` allows Web to reference only Application and Hosting, scans Web/Hosting source for forbidden database/infrastructure usage, and checks the resolved Web package graph for EF/SqlClient/Infrastructure packages.

- [x] **Step 6: Verify**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
dotnet test FlashInterview.slnx --filter WebProjectArchitectureTests
docker build -f src/FlashInterview.Api/Dockerfile --target build -t flash-interview-api-final-check .
docker build -f src/FlashInterview.Web/Dockerfile --target build -t flash-interview-web-final-check .
```

---

### Task 5: Remove Dependency Hygiene Warning And Final Verification

**Files:**
- Modify: `src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj`
- Test: full solution

- [x] **Step 1: Remove unnecessary package reference**

Removed the unused `Microsoft.Extensions.Hosting.Abstractions` package reference from Infrastructure.

- [x] **Step 2: Verify restore/build/test**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Expected: build and tests pass with zero warnings.

---

## Self-Review

- Spec coverage: the five architecture smells and follow-up review findings are mapped to current code locations.
- Placeholder scan: no stale API-local auth/user workflow paths remain.
- Type consistency: workflow contracts live in Application, Identity/EF implementations live in Infrastructure, hosting helpers live in `FlashInterview.Hosting`.
- Scope control: Web stays database-free; API remains the composition root; Infrastructure owns database/Identity access; no database schema or API route changes are required.
