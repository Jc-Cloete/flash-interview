# Architecture Smell Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the five architecture smells found in the review while preserving the current ASP.NET Core, MSSQL, Serilog, Swagger, and MVC-over-HTTP architecture.

**Architecture:** Keep `FlashInterview.Web` API-only and database-free. Move controller-owned workflows behind API-facing services, centralize cache invalidation around sensitive-word mutations, extract duplicated hosting setup into shared API/Web extension methods, and clean dependency drift without expanding scope.

**Tech Stack:** C#/.NET 10, ASP.NET Core MVC/Web API, ASP.NET Core Identity, EF Core SQL Server, xUnit, Serilog, OpenTelemetry, Swagger/Swashbuckle.

---

## File Structure

- `src/FlashInterview.Api/Auth/IAuthWorkflow.cs`: API-layer auth use-case interface.
- `src/FlashInterview.Api/Auth/AuthWorkflow.cs`: local and external sign-in orchestration currently in `AuthController`.
- `src/FlashInterview.Api/Auth/IdentityResultExtensions.cs`: shared Identity-to-ModelState helper.
- `src/FlashInterview.Api/Users/IUserManagementWorkflow.cs`: API-layer user-management use-case interface.
- `src/FlashInterview.Api/Users/UserManagementWorkflow.cs`: list/create/admin-role orchestration currently in `UsersController`.
- `src/FlashInterview.Api/SensitiveWords/ISensitiveWordAdministrationService.cs`: sensitive-word mutation/list interface.
- `src/FlashInterview.Api/SensitiveWords/SensitiveWordAdministrationService.cs`: repository calls plus cache invalidation.
- `src/FlashInterview.Api/Hosting/ObservabilityExtensions.cs`: shared API/Web compatible logging and OpenTelemetry registration.
- `src/FlashInterview.Api/Hosting/CorrelationExtensions.cs`: reusable correlation/session middleware helpers.
- `src/FlashInterview.Api/Controllers/AuthController.cs`: slim HTTP adapter.
- `src/FlashInterview.Api/Controllers/UsersController.cs`: slim HTTP adapter.
- `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`: slim HTTP adapter.
- `src/FlashInterview.Api/Program.cs`: use extracted hosting extensions.
- `src/FlashInterview.Web/Program.cs`: use extracted hosting extensions.
- `src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj`: remove unnecessary package reference.
- `tests/FlashInterview.Tests/AuthApiTests.cs`: keep existing behavior tests green; add targeted regression if behavior loses coverage.
- `tests/FlashInterview.Tests/ApiSurfaceTests.cs`: keep sensitive-word cache invalidation behavior green.
- `tests/FlashInterview.Tests/WebProjectArchitectureTests.cs`: keep Web isolation green.

---

### Task 1: Move Auth Workflow Out Of `AuthController`

**Files:**
- Create: `src/FlashInterview.Api/Auth/IAuthWorkflow.cs`
- Create: `src/FlashInterview.Api/Auth/AuthWorkflow.cs`
- Create: `src/FlashInterview.Api/Auth/IdentityResultExtensions.cs`
- Modify: `src/FlashInterview.Api/Controllers/AuthController.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`

- [ ] **Step 1: Add the auth workflow contract**

Create `IAuthWorkflow` with this public surface:

```csharp
using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Api.Auth;

public interface IAuthWorkflow
{
    Task<ActionResult<AuthenticatedUserDto>> LoginAsync(LoginRequest request, ModelStateDictionary modelState);

    Task<ActionResult<AuthenticatedUserDto>> ExternalSignInAsync(ExternalLoginRequest request, ModelStateDictionary modelState);
}
```

- [ ] **Step 2: Move Identity orchestration into `AuthWorkflow`**

Create `AuthWorkflow` and move the current logic from `AuthController.Login` and `AuthController.ExternalSignIn` into methods with the same behavior:

```csharp
public sealed class AuthWorkflow(
    UserManager<FlashInterviewUser> userManager,
    SignInManager<FlashInterviewUser> signInManager) : IAuthWorkflow
{
    private const string GoogleProvider = "Google";

    public async Task<ActionResult<AuthenticatedUserDto>> LoginAsync(LoginRequest request, ModelStateDictionary modelState)
    {
        // same trim, FindByEmailAsync, CheckPasswordSignInAsync, Unauthorized, and DTO mapping behavior as the current controller
    }

    public async Task<ActionResult<AuthenticatedUserDto>> ExternalSignInAsync(ExternalLoginRequest request, ModelStateDictionary modelState)
    {
        // same verified-email, provider validation, FindByLoginAsync, FindByEmailAsync, create/link/update behavior as the current controller
    }
}
```

When adding validation errors from Identity, use `IdentityResultExtensions.AddIdentityErrors(modelState, result)`.

- [ ] **Step 3: Slim `AuthController` to request parsing, validation, and delegation**

Keep `JsonElement` parsing behavior, Swagger attributes, routes, legacy endpoint, and HTTP responses. The controller should call:

```csharp
return await authWorkflow.LoginAsync(request, ModelState);
return await authWorkflow.ExternalSignInAsync(request, ModelState);
```

- [ ] **Step 4: Register the workflow**

Add this registration near the other API services:

```csharp
builder.Services.AddScoped<IAuthWorkflow, AuthWorkflow>();
```

- [ ] **Step 5: Verify and commit**

Run:

```bash
dotnet test FlashInterview.slnx --filter AuthApiTests
dotnet test FlashInterview.slnx
git add src/FlashInterview.Api/Auth src/FlashInterview.Api/Controllers/AuthController.cs src/FlashInterview.Api/Program.cs
git commit -m "refactor: move auth workflow out of controller"
```

Expected: all tests pass.

---

### Task 2: Move User Management Workflow Out Of `UsersController`

**Files:**
- Create: `src/FlashInterview.Api/Users/IUserManagementWorkflow.cs`
- Create: `src/FlashInterview.Api/Users/UserManagementWorkflow.cs`
- Modify: `src/FlashInterview.Api/Controllers/UsersController.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`

- [ ] **Step 1: Add user-management workflow contract**

Create an interface with:

```csharp
using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Api.Users;

public interface IUserManagementWorkflow
{
    Task<ActionResult<IReadOnlyList<UserListItemDto>>> ListAsync(CancellationToken cancellationToken);

    Task<ActionResult<UserListItemDto>> CreateAsync(CreateUserRequest request, ModelStateDictionary modelState);

    Task<ActionResult<UserListItemDto>> UpdateAdminRoleAsync(string id, UserRoleUpdateRequest request, ModelStateDictionary modelState);
}
```

- [ ] **Step 2: Move user and role orchestration into `UserManagementWorkflow`**

Move the current list/create/update-role logic from `UsersController` into `UserManagementWorkflow`, preserving:

- `FindByEmailAsync` conflict behavior.
- role creation before admin assignment.
- security-stamp update after role changes.
- locked-out calculation.
- ordered role list.
- current validation problem behavior for Identity errors.

- [ ] **Step 3: Slim `UsersController`**

The controller should keep route and Swagger attributes only and delegate:

```csharp
return await userManagementWorkflow.ListAsync(cancellationToken);
return await userManagementWorkflow.CreateAsync(request, ModelState);
return await userManagementWorkflow.UpdateAdminRoleAsync(id, request, ModelState);
```

- [ ] **Step 4: Register the workflow**

Add:

```csharp
builder.Services.AddScoped<IUserManagementWorkflow, UserManagementWorkflow>();
```

- [ ] **Step 5: Verify and commit**

Run:

```bash
dotnet test FlashInterview.slnx --filter AuthApiTests
dotnet test FlashInterview.slnx
git add src/FlashInterview.Api/Users src/FlashInterview.Api/Controllers/UsersController.cs src/FlashInterview.Api/Program.cs
git commit -m "refactor: move user management workflow out of controller"
```

Expected: all tests pass.

---

### Task 3: Centralize Sensitive-Word Cache Invalidation

**Files:**
- Create: `src/FlashInterview.Api/SensitiveWords/ISensitiveWordAdministrationService.cs`
- Create: `src/FlashInterview.Api/SensitiveWords/SensitiveWordAdministrationService.cs`
- Modify: `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [ ] **Step 1: Add administration service interface**

Create:

```csharp
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public interface ISensitiveWordAdministrationService
{
    Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken);
    Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement repository delegation plus invalidation**

Create a service that injects `ISensitiveWordRepository` and `ISensitiveWordMatcherCache`. It must call `matcherCache.Invalidate()` after successful create, after non-null update, and after successful delete.

- [ ] **Step 3: Slim `SensitiveWordsController`**

Replace direct `ISensitiveWordRepository` and `ISensitiveWordMatcherCache` dependencies with `ISensitiveWordAdministrationService`. Keep duplicate-word handling and all Swagger/route behavior unchanged.

- [ ] **Step 4: Register the service**

Add:

```csharp
builder.Services.AddScoped<ISensitiveWordAdministrationService, SensitiveWordAdministrationService>();
```

- [ ] **Step 5: Verify and commit**

Run:

```bash
dotnet test FlashInterview.slnx --filter ApiSurfaceTests
dotnet test FlashInterview.slnx
git add src/FlashInterview.Api/SensitiveWords src/FlashInterview.Api/Controllers/SensitiveWordsController.cs src/FlashInterview.Api/Program.cs
git commit -m "refactor: centralize sensitive word cache invalidation"
```

Expected: all tests pass, including cache invalidation tests around create/update/delete.

---

### Task 4: Extract Shared Hosting, Observability, And Correlation Setup

**Files:**
- Create: `src/FlashInterview.Api/Hosting/ObservabilityExtensions.cs`
- Create: `src/FlashInterview.Api/Hosting/CorrelationExtensions.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Modify: `src/FlashInterview.Web/Program.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`
- Test: `tests/FlashInterview.Tests/AuthWebTests.cs`

- [ ] **Step 1: Extract shared observability registration**

Create extension methods that accept service name, OTLP endpoint, application name, and optional API-specific metric/trace setup:

```csharp
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddFlashInterviewSerilog(this WebApplicationBuilder builder, string applicationName) { ... }

    public static WebApplicationBuilder AddFlashInterviewOpenTelemetry(
        this WebApplicationBuilder builder,
        string serviceName,
        string? otlpEndpoint,
        string applicationName,
        Action<MeterProviderBuilder>? configureMetrics = null,
        Action<TracerProviderBuilder>? configureTracing = null) { ... }
}
```

Preserve API-only EF tracing and `MaskingMetrics.MeterName`; preserve Web HTTP/runtime metrics and tracing.

- [ ] **Step 2: Extract correlation middleware**

Create:

```csharp
public static class CorrelationExtensions
{
    public static IApplicationBuilder UseFlashInterviewCorrelation(this WebApplication app) { ... }
}
```

Move the duplicated `X-Correlation-Id` / `X-Session-Id` validation, response echo, logger scope, and `LogContext.PushProperty` logic into the extension. Preserve allowed characters, max length 64, and fallback behavior.

- [ ] **Step 3: Update both entrypoints**

Replace duplicated setup in `Program.cs` files with calls to the new extensions. Keep API/Web behavior unchanged:

```csharp
builder.AddFlashInterviewSerilog("FlashInterview.Api");
builder.AddFlashInterviewOpenTelemetry(apiServiceName, apiOtlpEndpoint, "FlashInterview.Api", ...);
app.UseFlashInterviewCorrelation();
```

and equivalent Web calls.

- [ ] **Step 4: Verify and commit**

Run:

```bash
dotnet test FlashInterview.slnx --filter "ApiSurfaceTests|AuthWebTests"
dotnet test FlashInterview.slnx
git add src/FlashInterview.Api/Hosting src/FlashInterview.Api/Program.cs src/FlashInterview.Web/Program.cs
git commit -m "refactor: share hosting observability setup"
```

Expected: all tests pass; correlation headers still sanitize and echo correctly.

---

### Task 5: Remove Dependency Hygiene Warning And Final Verification

**Files:**
- Modify: `src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj`
- Test: full solution

- [ ] **Step 1: Remove unnecessary package reference**

Remove:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.7" />
```

from `src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj`.

- [ ] **Step 2: Verify restore/build/test**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Expected: no `NU1510` warning for `Microsoft.Extensions.Hosting.Abstractions`; build and tests pass.

- [ ] **Step 3: Commit**

Run:

```bash
git add src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj
git commit -m "chore: remove unnecessary hosting abstractions reference"
```

Expected: commit succeeds.

---

## Self-Review

- Spec coverage: all five architecture smells are mapped to tasks.
- Placeholder scan: no task is left as future work; examples with ellipses describe moved existing logic and require behavior preservation through existing tests.
- Type consistency: workflow/service interfaces match existing DTO and request types in `FlashInterview.Application`.
- Scope control: Web stays database-free; API remains the composition root for Infrastructure; no database schema or API route changes are planned.
