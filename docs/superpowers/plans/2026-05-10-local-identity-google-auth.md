# Local Identity And Google Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add production-shaped username/password authentication, optional Google sign-in/linking, initial super-admin bootstrap, and user-management controls without slowing the hot message-masking endpoint or letting MVC access MSSQL directly.

**Architecture:** `FlashInterview.Api` and `FlashInterview.Infrastructure` own the Identity SQL store, password verification, Google-login resolution/linking, role assignment, and user-management API. `FlashInterview.Web` remains database-free: it talks to API over HTTP, signs in its own MVC cookie from an authenticated user DTO returned by the API, and protects admin MVC routes with local cookie claims. `/api/messages/mask` remains anonymous/rate-limited for high-throughput use.

**Tech Stack:** ASP.NET Core IdentityCore, EF Core SQL Server, Microsoft Google authentication provider in Web for the OAuth challenge/callback, MVC cookie authentication, Web API controllers, xUnit, WebApplicationFactory, Swagger/OpenAPI, Serilog.

---

## File Structure

- Modify `src/FlashInterview.Infrastructure/FlashInterviewDbContext.cs`: derive from IdentityDbContext and keep existing sensitive-word schema.
- Create `src/FlashInterview.Infrastructure/Auth/FlashInterviewUser.cs`: application user with audit columns.
- Create `src/FlashInterview.Application/Auth/*.cs`: auth DTOs and role constants shared by API and Web.
- Create `src/FlashInterview.Infrastructure/Auth/InitialSuperAdminOptions.cs`: configuration object for bootstrap.
- Create `src/FlashInterview.Infrastructure/Auth/InitialSuperAdminBootstrapper.cs`: idempotent role/user seeding.
- Modify `src/FlashInterview.Infrastructure/DependencyInjection.cs`: register IdentityCore stores and bootstrap service.
- Modify `src/FlashInterview.Infrastructure/Migrations/*`: add EF migration for Identity tables.
- Create `src/FlashInterview.Api/Controllers/AuthController.cs`: internal Web-to-API auth operations.
- Create `src/FlashInterview.Api/Controllers/UsersController.cs`: internal user-management operations.
- Modify `src/FlashInterview.Api/OpenApi/SwaggerDocumentFilters.cs`: show API-key security on internal auth/user-management endpoints.
- Modify `src/FlashInterview.Web/Program.cs`: configure local MVC cookie, optional Google external provider, and policies.
- Create `src/FlashInterview.Web/Auth/AdminAuthorizationPolicies.cs`: Web policy names.
- Create `src/FlashInterview.Web/Clients/AuthApiClient.cs`: Web HTTP client for API auth/user operations.
- Create `src/FlashInterview.Web/Controllers/AccountController.cs`: local login/logout/register and Google challenge/callback/linking.
- Create `src/FlashInterview.Web/Controllers/UsersController.cs`: super-admin-only user management UI backed by API calls.
- Create `src/FlashInterview.Web/Models/AccountViewModels.cs`: login/register/external-link view models.
- Create `src/FlashInterview.Web/Models/UserManagementViewModels.cs`: user listing and role update view models.
- Create Razor views under `src/FlashInterview.Web/Views/Account/` and `src/FlashInterview.Web/Views/Users/`.
- Modify `src/FlashInterview.Web/Controllers/AdminController.cs`: require admin role/policy.
- Modify `src/FlashInterview.Web/Views/Shared/_Layout.cshtml`: login/logout/user-management navigation.
- Modify `tests/FlashInterview.Tests/WebProjectArchitectureTests.cs`: keep Web free of EF, SQL client, Infrastructure references, DbContext, and sensitive-word persistence access.
- Modify `tests/FlashInterview.Tests/AdminWebTests.cs`: assert Admin requires auth and layout exposes auth links.
- Create `tests/FlashInterview.Tests/AuthApiTests.cs`: local login/bootstrap/user-management API behavior.
- Create `tests/FlashInterview.Tests/AuthWebTests.cs`: MVC cookie login/logout and Google config behavior.
- Modify `README.md`, `docker-compose.dev.yml`, `deploy/release.env.example`, and `deploy/docker-compose.release.yml`: document auth settings and environment variables.

## Task 1: Identity Store, DTOs, And Super Admin Bootstrap

**Files:**
- Create: `src/FlashInterview.Application/Auth/ApplicationRoles.cs`
- Create: `src/FlashInterview.Application/Auth/AuthenticatedUserDto.cs`
- Create: `src/FlashInterview.Application/Auth/AuthRequests.cs`
- Create: `src/FlashInterview.Application/Auth/UserManagementDtos.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/FlashInterviewUser.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/InitialSuperAdminOptions.cs`
- Create: `src/FlashInterview.Infrastructure/Auth/InitialSuperAdminBootstrapper.cs`
- Modify: `src/FlashInterview.Infrastructure/FlashInterviewDbContext.cs`
- Modify: `src/FlashInterview.Infrastructure/DependencyInjection.cs`
- Modify: `src/FlashInterview.Infrastructure/FlashInterview.Infrastructure.csproj`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`

- [ ] **Step 1: Write failing bootstrap tests**
  - Assert bootstrap creates the `SuperAdmin` and `Admin` roles.
  - Assert a configured email/password creates or updates a local user, marks email confirmed, and assigns both roles.
  - Assert rerunning bootstrap is idempotent.

- [ ] **Step 2: Run focused test and verify RED**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter AuthApiTests`
  - Expected: FAIL because Identity types and bootstrapper do not exist yet.

- [ ] **Step 3: Implement Identity infrastructure**
  - Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` to Infrastructure.
  - Make `FlashInterviewDbContext` inherit `IdentityDbContext<FlashInterviewUser>`.
  - Call `base.OnModelCreating(modelBuilder)` before sensitive-word mapping.
  - Register `AddIdentityCore<FlashInterviewUser>()`, role support, EF stores, `SignInManager`, and default token providers in Infrastructure.
  - Add `InitialSuperAdminBootstrapper` as a hosted service.

- [ ] **Step 4: Run focused test and verify GREEN**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter AuthApiTests`
  - Expected: PASS for bootstrap tests.

- [ ] **Step 5: Create migration**
  - Run: `dotnet ef migrations add AddIdentityAuth --project src/FlashInterview.Infrastructure --startup-project src/FlashInterview.Api`
  - Expected: new migration adds ASP.NET Identity tables without dropping or rewriting `SensitiveWords`.

## Task 2: Internal Auth API And Web Cookie Login

**Files:**
- Create: `src/FlashInterview.Api/Controllers/AuthController.cs`
- Modify: `src/FlashInterview.Api/OpenApi/SwaggerDocumentFilters.cs`
- Modify: `src/FlashInterview.Web/Program.cs`
- Create: `src/FlashInterview.Web/Auth/AdminAuthorizationPolicies.cs`
- Create: `src/FlashInterview.Web/Clients/AuthApiClient.cs`
- Create: `src/FlashInterview.Web/Controllers/AccountController.cs`
- Create: `src/FlashInterview.Web/Models/AccountViewModels.cs`
- Create: `src/FlashInterview.Web/Views/Account/Login.cshtml`
- Create: `src/FlashInterview.Web/Views/Account/Register.cshtml`
- Modify: `src/FlashInterview.Web/Views/Shared/_Layout.cshtml`
- Modify: `src/FlashInterview.Web/Controllers/AdminController.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`
- Test: `tests/FlashInterview.Tests/AuthWebTests.cs`

- [ ] **Step 1: Write failing API and Web auth tests**
  - API: `POST /api/auth/login` rejects missing/invalid API key.
  - API: `POST /api/auth/login` rejects bad password and returns user id/email/roles for valid credentials.
  - Web: `/Admin` redirects unauthenticated users to `/Account/Login`.
  - Web: posting valid credentials signs in a local MVC cookie and allows `/Admin`.
  - Web: logout clears admin access.

- [ ] **Step 2: Run focused tests and verify RED**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: FAIL because auth endpoints and Web cookie flow do not exist.

- [ ] **Step 3: Implement internal auth API**
  - Add API-key-protected `AuthController`.
  - Use `SignInManager.CheckPasswordSignInAsync` for password verification.
  - Return `AuthenticatedUserDto` with app user id, email, display name, and roles.
  - Do not return password hashes, security stamps, or tokens.

- [ ] **Step 4: Implement Web cookie login**
  - Configure Web cookie auth with login path `/Account/Login`, access denied path `/Account/AccessDenied`, HttpOnly, secure policy, same-site lax, sliding expiration.
  - `AccountController.Login` calls `AuthApiClient.LoginAsync`, then signs in claims containing user id, email, and roles.
  - `AccountController.Logout` signs out only the Web cookie.
  - `AdminController` requires `Admin` or `SuperAdmin`.
  - `ChatController` remains anonymous.

- [ ] **Step 5: Run focused tests and verify GREEN**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: PASS.

## Task 3: Google Sign-In And Account Provisioning

**Files:**
- Modify: `src/FlashInterview.Web/Program.cs`
- Modify: `src/FlashInterview.Web/Controllers/AccountController.cs`
- Modify: `src/FlashInterview.Web/Views/Account/Login.cshtml`
- Modify: `src/FlashInterview.Web/Clients/AuthApiClient.cs`
- Modify: `src/FlashInterview.Api/Controllers/AuthController.cs`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`
- Test: `tests/FlashInterview.Tests/AuthWebTests.cs`

- [ ] **Step 1: Write failing Google/linking tests**
  - Web hides Google button when client id or secret is missing.
  - Web shows Google button when both are configured.
  - API can sign in an existing external login.
  - API can link a verified Google email to an existing local user without password confirmation.
  - API can create a plain non-admin user for a new verified Google email.
  - API rejects external sign-in when the provider email is unverified.

- [ ] **Step 2: Run focused tests and verify RED**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: FAIL for missing Google/linking behavior.

- [ ] **Step 3: Implement Google Web challenge/callback**
  - Configure `AddGoogle` only when both `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are configured.
  - Login view renders Google button only when configured.
  - Callback extracts provider key, email, email_verified, and display name from Google claims.
  - Web calls API to sign in, link, or provision the external login and signs in the returned user DTO.

- [ ] **Step 4: Implement API external sign-in operation**
  - Add API-key-protected endpoint for external sign-in.
  - Use Identity `FindByLoginAsync`, `FindByEmailAsync`, `CreateAsync`, and `AddLoginAsync`.
  - Require verified provider email before linking by email or creating a plain user.

- [ ] **Step 5: Run focused tests and verify GREEN**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: PASS.

## Task 4: User Management Controls

**Files:**
- Create: `src/FlashInterview.Api/Controllers/UsersController.cs`
- Modify: `src/FlashInterview.Web/Clients/AuthApiClient.cs`
- Create: `src/FlashInterview.Web/Controllers/UsersController.cs`
- Create: `src/FlashInterview.Web/Models/UserManagementViewModels.cs`
- Create: `src/FlashInterview.Web/Views/Users/Index.cshtml`
- Create: `src/FlashInterview.Web/Views/Users/Edit.cshtml`
- Test: `tests/FlashInterview.Tests/AuthApiTests.cs`
- Test: `tests/FlashInterview.Tests/AuthWebTests.cs`

- [ ] **Step 1: Write failing user-management tests**
  - API requires internal API key for user-management endpoints.
  - API lists users with email, lockout state, and roles.
  - API grants/revokes `Admin`.
  - API prevents removing the last `SuperAdmin`.
  - Web `/Users` requires `SuperAdmin`.

- [ ] **Step 2: Run focused tests and verify RED**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: FAIL for missing user-management endpoints and views.

- [ ] **Step 3: Implement API user management**
  - Use `UserManager<FlashInterviewUser>` and `RoleManager<IdentityRole>`.
  - Page users in deterministic email order.
  - Allow changing `Admin`; keep `SuperAdmin` bootstrap-controlled.
  - Prevent removing/disabling the last super admin.

- [ ] **Step 4: Implement Web user-management UI**
  - Use Web `SuperAdmin` policy.
  - Call API through `AuthApiClient`.
  - Render users and role controls without direct database access.

- [ ] **Step 5: Run focused tests and verify GREEN**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter "AuthApiTests|AuthWebTests"`
  - Expected: PASS.

## Task 5: Architecture Guards, Config, Docs, And Full Verification

**Files:**
- Modify: `tests/FlashInterview.Tests/WebProjectArchitectureTests.cs`
- Modify: `README.md`
- Modify: `docker-compose.dev.yml`
- Modify: `deploy/release.env.example`
- Modify: `deploy/docker-compose.release.yml`
- Modify: `src/FlashInterview.Web/appsettings.json`
- Modify: `src/FlashInterview.Web/appsettings.Development.json`
- Modify: `src/FlashInterview.Api/appsettings.json`
- Modify: `src/FlashInterview.Api/appsettings.Development.json`

- [ ] **Step 1: Update architecture guard tests**
  - Continue failing if Web references Infrastructure, EF Core, SQL client packages, `FlashInterviewDbContext`, or sensitive-word persistence implementation.
  - Permit Web only to use Application DTOs/contracts and HTTP API clients.

- [ ] **Step 2: Run architecture tests**
  - Run: `dotnet test FlashInterview.slnx --no-restore --filter WebProjectArchitectureTests`
  - Expected: PASS.

- [ ] **Step 3: Document configuration**
  - Add settings:
    - `Authentication:Google:ClientId`
    - `Authentication:Google:ClientSecret`
    - `Security:InitialSuperAdmin:Enabled`
    - `Security:InitialSuperAdmin:Email`
    - `Security:InitialSuperAdmin:Password`
  - Document that `/api/messages/mask` remains anonymous/rate-limited for high-throughput use.
  - Document that API/Infrastructure own Identity SQL persistence and Web uses HTTP plus local cookies.

- [ ] **Step 4: Run full verification**
  - Run: `dotnet restore FlashInterview.slnx`
  - Run: `dotnet build FlashInterview.slnx --no-restore`
  - Run: `dotnet test FlashInterview.slnx --no-build`
  - Expected: all commands exit 0, with MSSQL tests skipped unless local SQL Server is available.
