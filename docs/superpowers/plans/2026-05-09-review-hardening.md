# Review Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the senior-engineer review findings into production-hardening improvements for readiness, CI confidence, masking performance, deployment ergonomics, and demo completeness.

**Architecture:** Keep the existing layer boundaries: API owns persistence and operational endpoints, infrastructure owns EF Core/MSSQL, application owns deterministic masking behavior, and MVC continues to call the API over HTTP only. Add focused services where they reduce real operational risk, and keep all changes covered by targeted tests before implementation.

**Tech Stack:** .NET 10, ASP.NET Core Web API/MVC, EF Core SQL Server, xUnit, Swashbuckle, Serilog, Docker Compose, GitHub Actions.

---

## Review Findings To Address

- `/readyz` currently behaves like a liveness check and does not verify MSSQL dependency readiness.
- MSSQL-backed CRUD/migration/seed tests are optional locally and likely skipped in GitHub Actions because SQL Server is not started in CI.
- `POST /api/messages/mask` queries all active sensitive words and rebuilds regex patterns on every request.
- Rate limiting uses `RemoteIpAddress` directly, which is fragile behind reverse proxies unless forwarded headers are configured.
- Production-style first run requires manual knowledge of migration/seed flags, but the repo lacks a clearly scripted evaluator path.
- Swagger is only available in Development; that is acceptable if documented, but the reviewer experience should be explicit.
- Mock Chat meets the minimum submission flow but misses quick seed/example messages called out in the spec.

## File Structure

- Modify: `src/FlashInterview.Api/Program.cs`
  - Register SQL Server readiness, forwarded headers, cache invalidation services, and Swagger behavior.
- Modify: `src/FlashInterview.Api/FlashInterview.Api.csproj`
  - Add the SQL Server health-check package used by `/readyz`.
- Modify: `src/FlashInterview.Api/Controllers/MessagesController.cs`
  - Use a cached/precompiled masking provider instead of querying active candidates directly.
- Create: `src/FlashInterview.Application/SensitiveWords/CompiledSensitiveWordMasker.cs`
  - Immutable precompiled matcher built from active candidates.
- Modify: `src/FlashInterview.Application/SensitiveWords/SensitiveWordMasker.cs`
  - Delegate existing public behavior to compiled matcher or share pattern-building helpers.
- Create: `src/FlashInterview.Api/SensitiveWords/ISensitiveWordMatcherCache.cs`
  - API-layer abstraction for cached active matcher.
- Create: `src/FlashInterview.Api/SensitiveWords/SensitiveWordMatcherCache.cs`
  - Scoped/singleton cache that refreshes active candidates from the repository and invalidates after CRUD writes.
- Modify: `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`
  - Invalidate the matcher cache after create, update, and delete.
- Modify: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`
  - Add readiness, forwarded-header rate-limit, and cache invalidation coverage with fake repository.
- Modify: `tests/FlashInterview.Tests/SensitiveWordMaskerTests.cs`
  - Add compiled matcher parity and repeated-use tests.
- Modify: `.github/workflows/pr-checks.yml`
  - Add a SQL Server service container and configure `FLASHINTERVIEW_TEST_MSSQL_MASTER` so MSSQL tests run in CI.
- Modify: `docker-compose.yml`
  - Add health checks and service dependency conditions for production-shaped local smoke tests.
- Modify: `docker-compose.dev.yml`
  - Add health checks and service dependency conditions for development startup.
- Modify: `deploy/docker-compose.release.yml`
  - Add health checks and dependency conditions for release bundle usability.
- Modify: `src/FlashInterview.Api/Dockerfile`
  - Install `curl` for container health checks.
- Modify: `src/FlashInterview.Api/Dockerfile.dev`
  - Install `curl` for container health checks.
- Create: `scripts/run-local-smoke.sh`
  - One-command evaluator path for production-style compose with migration and seed enabled.
- Modify: `README.md`
  - Document readiness semantics, CI MSSQL enforcement, cached masking behavior, forwarded-header expectations, and smoke command.
- Modify: `src/FlashInterview.Web/Views/Chat/Index.cshtml`
  - Add example-message buttons/links that prefill useful SQL-sensitive samples without changing the API contract.
- Modify: `tests/FlashInterview.Tests/AdminWebTests.cs`
  - Add a view-level assertion for example messages on the mock chat page.

---

### Task 1: Real Readiness Endpoint

**Files:**
- Modify: `src/FlashInterview.Api/FlashInterview.Api.csproj`
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [x] **Step 1: Write failing readiness tests**

Add these tests to `tests/FlashInterview.Tests/ApiSurfaceTests.cs`:

```csharp
[Fact]
public async Task HealthEndpoint_ReturnsOkWithoutDatabaseCheck()
{
    using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
    using var client = factory.CreateHttpsClient();

    using var response = await client.GetAsync("/healthz");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task ReadinessEndpoint_ReturnsServiceUnavailableWhenSqlServerIsUnavailable()
{
    using var factory = new FlashInterviewApiFactory(
        new FakeSensitiveWordRepository(),
        connectionString: "Server=127.0.0.1,1;Database=FlashInterview;User Id=sa;Password=bad;TrustServerCertificate=True;Encrypt=True;Connect Timeout=1");
    using var client = factory.CreateHttpsClient();

    using var response = await client.GetAsync("/readyz");

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}
```

Update the nested `FlashInterviewApiFactory` constructor in the same file to accept the optional connection string:

```csharp
private sealed class FlashInterviewApiFactory(
    ISensitiveWordRepository repository,
    string? adminApiKey = null,
    int? rateLimitPermitLimit = null,
    int? rateLimitWindowSeconds = null,
    string environment = "Production",
    string? connectionString = null) : WebApplicationFactory<Program>
```

Inside `ConfigureAppConfiguration`, add:

```csharp
if (connectionString is not null)
{
    configuration["ConnectionStrings:DefaultConnection"] = connectionString;
}
```

- [x] **Step 2: Run the failing readiness test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~ReadinessEndpoint_ReturnsServiceUnavailableWhenSqlServerIsUnavailable"
```

Expected: FAIL because `/readyz` currently returns `200 OK`.

- [x] **Step 3: Implement tagged health checks**

Add the SQL Server health-check package:

```bash
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package AspNetCore.HealthChecks.SqlServer
```

In `src/FlashInterview.Api/Program.cs`, replace:

```csharp
builder.Services.AddHealthChecks();
```

with:

```csharp
builder.Services
    .AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required."),
        name: "mssql",
        tags: ["ready"]);
```

Add this using:

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
```

Replace:

```csharp
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/readyz");
```

with:

```csharp
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

- [x] **Step 4: Run readiness tests**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~HealthEndpoint_ReturnsOkWithoutDatabaseCheck|FullyQualifiedName~ReadinessEndpoint_ReturnsServiceUnavailableWhenSqlServerIsUnavailable"
```

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/FlashInterview.Api/Program.cs tests/FlashInterview.Tests/ApiSurfaceTests.cs
git commit -m "fix: make readiness verify sql server"
```

---

### Task 2: Run MSSQL Integration Tests In CI

**Files:**
- Modify: `.github/workflows/pr-checks.yml`

- [x] **Step 1: Update CI to start SQL Server**

In `.github/workflows/pr-checks.yml`, under the `dotnet` job and before `steps:`, add:

```yaml
    services:
      mssql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: "Y"
          MSSQL_SA_PASSWORD: "Your_strong_password123"
          MSSQL_PID: "Developer"
        ports:
          - 1433:1433
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Your_strong_password123 -C -Q 'SELECT 1' || exit 1"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 20
```

In the `Test` step, add:

```yaml
        env:
          FLASHINTERVIEW_TEST_MSSQL_MASTER: "Server=localhost,1433;Database=master;User Id=sa;Password=Your_strong_password123;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5"
```

- [x] **Step 2: Validate workflow syntax locally**

Run:

```bash
python3 - <<'PY'
from pathlib import Path
text = Path(".github/workflows/pr-checks.yml").read_text()
assert "services:" in text
assert "mcr.microsoft.com/mssql/server:2022-latest" in text
assert "FLASHINTERVIEW_TEST_MSSQL_MASTER" in text
print("workflow contains MSSQL CI service")
PY
```

Expected: `workflow contains MSSQL CI service`.

- [x] **Step 3: Run local verification**

Run:

```bash
dotnet test FlashInterview.slnx --no-build
```

Expected: PASS. If local SQL Server is available, MSSQL tests run; otherwise local tests may skip, while CI will run them because the service is configured.

- [x] **Step 4: Commit**

Run:

```bash
git add .github/workflows/pr-checks.yml
git commit -m "ci: run mssql integration tests"
```

---

### Task 3: Cache And Reuse Compiled Masking Rules

**Files:**
- Create: `src/FlashInterview.Application/SensitiveWords/CompiledSensitiveWordMasker.cs`
- Modify: `src/FlashInterview.Application/SensitiveWords/SensitiveWordMasker.cs`
- Create: `src/FlashInterview.Api/SensitiveWords/ISensitiveWordMatcherCache.cs`
- Create: `src/FlashInterview.Api/SensitiveWords/SensitiveWordMatcherCache.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Modify: `src/FlashInterview.Api/Controllers/MessagesController.cs`
- Modify: `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`
- Test: `tests/FlashInterview.Tests/SensitiveWordMaskerTests.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [x] **Step 1: Write compiled matcher tests**

Add to `tests/FlashInterview.Tests/SensitiveWordMaskerTests.cs`:

```csharp
[Fact]
public void CompiledMasker_ReusesPreparedPatternsAcrossMessages()
{
    var compiled = CompiledSensitiveWordMasker.FromCandidates(
    [
        new SensitiveWordCandidate("DROP"),
        new SensitiveWordCandidate("SELECT * FROM")
    ]);

    var first = compiled.Mask("DROP table users");
    var second = compiled.Mask("SELECT * FROM users");

    Assert.Equal("**** table users", first.MaskedMessage);
    Assert.Equal("************* users", second.MaskedMessage);
}
```

- [x] **Step 2: Run the failing compiled matcher test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~CompiledMasker_ReusesPreparedPatternsAcrossMessages"
```

Expected: FAIL because `CompiledSensitiveWordMasker` does not exist.

- [x] **Step 3: Add compiled matcher**

Create `src/FlashInterview.Application/SensitiveWords/CompiledSensitiveWordMasker.cs`:

```csharp
using System.Text.RegularExpressions;

namespace FlashInterview.Application.SensitiveWords;

public sealed class CompiledSensitiveWordMasker
{
    private readonly PreparedSensitiveWord[] preparedWords;

    private CompiledSensitiveWordMasker(PreparedSensitiveWord[] preparedWords)
    {
        this.preparedWords = preparedWords;
    }

    public static CompiledSensitiveWordMasker FromCandidates(IEnumerable<SensitiveWordCandidate> sensitiveWords)
    {
        var preparedWords = sensitiveWords
            .Select(word => word.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .Select(value => new PreparedSensitiveWord(value, new Regex(
                BuildPattern(value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)))
            .ToArray();

        return new CompiledSensitiveWordMasker(preparedWords);
    }

    public MaskMessageResult Mask(string message)
    {
        if (message.Length == 0)
        {
            return new MaskMessageResult(message, message, []);
        }

        var reserved = new bool[message.Length];
        var matches = new List<SensitiveWordMatch>();

        foreach (var preparedWord in preparedWords)
        {
            foreach (Match match in preparedWord.Regex.Matches(message))
            {
                if (IsRangeReserved(reserved, match.Index, match.Length))
                {
                    continue;
                }

                MarkRangeReserved(reserved, match.Index, match.Length);
                matches.Add(new SensitiveWordMatch(preparedWord.Value, match.Index, match.Index + match.Length));
            }
        }

        if (matches.Count == 0)
        {
            return new MaskMessageResult(message, message, []);
        }

        var masked = message.ToCharArray();
        foreach (var match in matches)
        {
            for (var index = match.Start; index < match.End; index++)
            {
                masked[index] = '*';
            }
        }

        return new MaskMessageResult(message, new string(masked), matches.OrderBy(match => match.Start).ToArray());
    }

    internal static string BuildPattern(string candidate)
    {
        var escaped = Regex.Escape(candidate);
        return IsSingleWord(candidate)
            ? $@"(?<![A-Za-z0-9_]){escaped}(?![A-Za-z0-9_])"
            : escaped;
    }

    private static bool IsSingleWord(string candidate)
    {
        return candidate.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static bool IsRangeReserved(bool[] reserved, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (reserved[index])
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkRangeReserved(bool[] reserved, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            reserved[index] = true;
        }
    }

    private sealed record PreparedSensitiveWord(string Value, Regex Regex);
}
```

Modify `src/FlashInterview.Application/SensitiveWords/SensitiveWordMasker.cs` so `Mask` delegates:

```csharp
public sealed class SensitiveWordMasker
{
    public MaskMessageResult Mask(string message, IEnumerable<SensitiveWordCandidate> sensitiveWords)
    {
        return CompiledSensitiveWordMasker.FromCandidates(sensitiveWords).Mask(message);
    }
}
```

- [x] **Step 4: Add API matcher cache**

Create `src/FlashInterview.Api/SensitiveWords/ISensitiveWordMatcherCache.cs`:

```csharp
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public interface ISensitiveWordMatcherCache
{
    Task<CompiledSensitiveWordMasker> GetAsync(CancellationToken cancellationToken);

    void Invalidate();
}
```

Create `src/FlashInterview.Api/SensitiveWords/SensitiveWordMatcherCache.cs`:

```csharp
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public sealed class SensitiveWordMatcherCache(IServiceScopeFactory scopeFactory) : ISensitiveWordMatcherCache
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private volatile CompiledSensitiveWordMasker? cachedMasker;

    public async Task<CompiledSensitiveWordMasker> GetAsync(CancellationToken cancellationToken)
    {
        var current = cachedMasker;
        if (current is not null)
        {
            return current;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            current = cachedMasker;
            if (current is not null)
            {
                return current;
            }

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISensitiveWordRepository>();
            var candidates = await repository.ListActiveCandidatesAsync(cancellationToken);
            current = CompiledSensitiveWordMasker.FromCandidates(candidates);
            cachedMasker = current;
            return current;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        cachedMasker = null;
    }
}
```

- [x] **Step 5: Wire cache into API**

In `src/FlashInterview.Api/Program.cs`, add:

```csharp
using FlashInterview.Api.SensitiveWords;
```

Replace:

```csharp
builder.Services.AddSingleton<SensitiveWordMasker>();
```

with:

```csharp
builder.Services.AddSingleton<ISensitiveWordMatcherCache, SensitiveWordMatcherCache>();
```

In `src/FlashInterview.Api/Controllers/MessagesController.cs`, replace the constructor dependencies:

```csharp
public sealed class MessagesController(
    ISensitiveWordRepository repository,
    SensitiveWordMasker masker) : ControllerBase
```

with:

```csharp
public sealed class MessagesController(ISensitiveWordMatcherCache matcherCache) : ControllerBase
```

Add:

```csharp
using FlashInterview.Api.SensitiveWords;
```

Replace the action body:

```csharp
var candidates = await repository.ListActiveCandidatesAsync(cancellationToken);
var result = masker.Mask(request.Message, candidates);
```

with:

```csharp
var matcher = await matcherCache.GetAsync(cancellationToken);
var result = matcher.Mask(request.Message);
```

In `src/FlashInterview.Api/Controllers/SensitiveWordsController.cs`, add the cache dependency:

```csharp
public sealed class SensitiveWordsController(
    ISensitiveWordRepository repository,
    ISensitiveWordMatcherCache matcherCache) : ControllerBase
```

Add:

```csharp
using FlashInterview.Api.SensitiveWords;
```

After successful create/update/delete operations, call:

```csharp
matcherCache.Invalidate();
```

For delete, call it only when `deleted` is true.

- [x] **Step 6: Run masking and API tests**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~SensitiveWordMaskerTests|FullyQualifiedName~MaskEndpoint_MasksMessageUsingActiveRepositoryCandidates"
```

Expected: PASS.

- [x] **Step 7: Commit**

Run:

```bash
git add src/FlashInterview.Application/SensitiveWords src/FlashInterview.Api tests/FlashInterview.Tests
git commit -m "perf: cache compiled sensitive word matcher"
```

---

### Task 4: Forwarded Headers For Correct Rate-Limit Identity

**Files:**
- Modify: `src/FlashInterview.Api/Program.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [x] **Step 1: Write forwarded-header rate-limit test**

Add to `tests/FlashInterview.Tests/ApiSurfaceTests.cs`:

```csharp
[Fact]
public async Task MaskEndpoint_RateLimitUsesForwardedClientIpWhenConfiguredByProxy()
{
    using var factory = new FlashInterviewApiFactory(
        new FakeSensitiveWordRepository(),
        rateLimitPermitLimit: 1,
        rateLimitWindowSeconds: 60);
    using var client = factory.CreateHttpsClient();

    using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/messages/mask")
    {
        Content = JsonContent.Create(new MaskMessageRequest("DROP"))
    };
    firstRequest.Headers.Add("X-Forwarded-For", "203.0.113.10");

    using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/messages/mask")
    {
        Content = JsonContent.Create(new MaskMessageRequest("DROP"))
    };
    secondRequest.Headers.Add("X-Forwarded-For", "203.0.113.20");

    using var firstResponse = await client.SendAsync(firstRequest);
    using var secondResponse = await client.SendAsync(secondRequest);

    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
}
```

- [x] **Step 2: Run the failing forwarded-header test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~MaskEndpoint_RateLimitUsesForwardedClientIpWhenConfiguredByProxy"
```

Expected: FAIL because both requests share the same remote address in the test server.

- [x] **Step 3: Configure forwarded headers**

In `src/FlashInterview.Api/Program.cs`, add:

```csharp
using Microsoft.AspNetCore.HttpOverrides;
```

Before `var app = builder.Build();`, add:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
```

Before `app.UseSerilogRequestLogging(...)`, add:

```csharp
app.UseForwardedHeaders();
```

- [x] **Step 4: Run forwarded-header and rate-limit tests**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~MaskEndpoint_RateLimit"
```

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/FlashInterview.Api/Program.cs tests/FlashInterview.Tests/ApiSurfaceTests.cs
git commit -m "fix: honor forwarded client ip for rate limits"
```

---

### Task 5: Docker Health Checks And Evaluator Smoke Script

**Files:**
- Modify: `docker-compose.yml`
- Modify: `docker-compose.dev.yml`
- Modify: `deploy/docker-compose.release.yml`
- Modify: `src/FlashInterview.Api/Dockerfile`
- Modify: `src/FlashInterview.Api/Dockerfile.dev`
- Create: `scripts/run-local-smoke.sh`
- Modify: `README.md`

- [x] **Step 1: Install curl in API images**

In `src/FlashInterview.Api/Dockerfile`, add this in the runtime stage after `FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime`:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
```

In `src/FlashInterview.Api/Dockerfile.dev`, add this after the `FROM` line:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
```

- [x] **Step 2: Add Compose health checks**

In each compose file, add this to the `mssql` service:

```yaml
    healthcheck:
      test: [ "CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$${MSSQL_SA_PASSWORD}\" -C -Q 'SELECT 1' || exit 1" ]
      interval: 10s
      timeout: 5s
      retries: 20
      start_period: 20s
```

Change API `depends_on` from list form:

```yaml
    depends_on:
      - mssql
```

to:

```yaml
    depends_on:
      mssql:
        condition: service_healthy
```

Add this to the `api` service:

```yaml
    healthcheck:
      test: [ "CMD-SHELL", "curl -fsS http://localhost:8080/healthz || exit 1" ]
      interval: 10s
      timeout: 5s
      retries: 12
      start_period: 10s
```

Change Web `depends_on` from list form:

```yaml
    depends_on:
      - api
```

to:

```yaml
    depends_on:
      api:
        condition: service_healthy
```

- [x] **Step 3: Add local smoke script**

Create `scripts/run-local-smoke.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

export MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-Your_strong_password123}"
export FLASHINTERVIEW_ADMIN_API_KEY="${FLASHINTERVIEW_ADMIN_API_KEY:-local-smoke-admin-key}"
export DATABASE_APPLY_MIGRATIONS_ON_STARTUP=true
export DATABASE_SEED_ON_STARTUP=true

docker compose up --build -d

printf 'Waiting for API readiness'
for _ in {1..60}; do
  if curl -fsS http://localhost:8080/readyz >/dev/null; then
    printf '\nAPI ready: http://localhost:8080\n'
    printf 'MVC ready: http://localhost:8081\n'
    exit 0
  fi
  printf '.'
  sleep 2
done

printf '\nAPI did not become ready. Recent API logs:\n'
docker compose logs --tail=100 api
exit 1
```

Run:

```bash
chmod +x scripts/run-local-smoke.sh
```

- [x] **Step 4: Validate compose and script syntax**

Run:

```bash
MSSQL_SA_PASSWORD=Test_password123 FLASHINTERVIEW_ADMIN_API_KEY=test-key docker compose config --quiet
docker compose -f docker-compose.dev.yml config --quiet
MSSQL_SA_PASSWORD=Test_password123 FLASHINTERVIEW_ADMIN_API_KEY=test-key FLASH_INTERVIEW_API_IMAGE=api:test FLASH_INTERVIEW_WEB_IMAGE=web:test docker compose -f deploy/docker-compose.release.yml config --quiet
bash -n scripts/run-local-smoke.sh
```

Expected: all commands exit `0`.

- [x] **Step 5: Document smoke path**

In `README.md`, under `Production-Style Compose`, add:

````markdown
For an evaluator-friendly local smoke test that builds images, starts MSSQL, applies migrations, seeds the supplied sensitive-word list, and waits for API readiness:

```bash
./scripts/run-local-smoke.sh
```

This command uses local development defaults unless `MSSQL_SA_PASSWORD` or `FLASHINTERVIEW_ADMIN_API_KEY` are already set in the shell.
````

- [x] **Step 6: Commit**

Run:

```bash
git add docker-compose.yml docker-compose.dev.yml deploy/docker-compose.release.yml src/FlashInterview.Api/Dockerfile src/FlashInterview.Api/Dockerfile.dev scripts/run-local-smoke.sh README.md
git commit -m "chore: add compose health checks and smoke script"
```

---

### Task 6: Mock Chat Example Messages

**Files:**
- Modify: `src/FlashInterview.Web/Views/Chat/Index.cshtml`
- Test: `tests/FlashInterview.Tests/AdminWebTests.cs`

- [x] **Step 1: Write failing view assertion**

Add to `tests/FlashInterview.Tests/AdminWebTests.cs`:

```csharp
[Fact]
public void ChatIndexView_ContainsSeedExampleMessages()
{
    var viewPath = Path.Combine(
        TestPaths.RepositoryRoot,
        "src",
        "FlashInterview.Web",
        "Views",
        "Chat",
        "Index.cshtml");

    var content = File.ReadAllText(viewPath);

    Assert.Contains("SELECT * FROM users", content, StringComparison.Ordinal);
    Assert.Contains("drop table users", content, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("data-example-message", content, StringComparison.Ordinal);
}
```

- [x] **Step 2: Run the failing view test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~ChatIndexView_ContainsSeedExampleMessages"
```

Expected: FAIL because the view has no example messages.

- [x] **Step 3: Add example buttons and small script**

Modify `src/FlashInterview.Web/Views/Chat/Index.cshtml`:

```cshtml
@model FlashInterview.Web.Models.ChatViewModel

@{
    ViewData["Title"] = "Mock Chat";
    var examples = new[]
    {
        "SELECT * FROM users",
        "drop table users",
        "DROPLET should not mask DROP inside another word"
    };
}

<h1>Mock Chat</h1>

<div class="mb-3">
    @foreach (var example in examples)
    {
        <button class="btn btn-outline-secondary btn-sm me-2 mb-2" type="button" data-example-message="@example">@example</button>
    }
</div>

<form asp-action="Index" method="post" class="mb-4">
    <label class="form-label" for="message">Message</label>
    <textarea class="form-control mb-3" id="message" name="message" rows="4" maxlength="10000" required>@Model.Message</textarea>
    <button class="btn btn-primary" type="submit">Bloop message</button>
</form>

@if (Model.Result is not null)
{
    <section class="border rounded p-3">
        <h2 class="h5">Result</h2>
        <p><strong>Original:</strong> @Model.Result.OriginalMessage</p>
        <p><strong>Masked:</strong> @Model.Result.MaskedMessage</p>
    </section>
}

@section Scripts {
    <script>
        for (const button of document.querySelectorAll("[data-example-message]")) {
            button.addEventListener("click", () => {
                document.getElementById("message").value = button.dataset.exampleMessage;
            });
        }
    </script>
}
```

- [x] **Step 4: Run view test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~ChatIndexView_ContainsSeedExampleMessages"
```

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/FlashInterview.Web/Views/Chat/Index.cshtml tests/FlashInterview.Tests/AdminWebTests.cs
git commit -m "feat: add mock chat examples"
```

---

### Task 7: README Review Notes And Swagger Positioning

**Files:**
- Modify: `README.md`

- [x] **Step 1: Document operational choices**

In `README.md`, update these sections:

Under `Health endpoints`, replace the existing two bullets with:

```markdown
- `GET /healthz`: process liveness only; it does not require MSSQL.
- `GET /readyz`: readiness check that verifies the API can reach MSSQL.
```

Under `API Surface`, keep the Swagger note and add:

```markdown
Swagger UI is intentionally enabled only for Development in this submission. The OpenAPI generation is covered by tests, and production deployments can expose the JSON/UI behind authenticated internal access if the hosting environment requires live API documentation.
```

Under `Test Coverage`, add:

```markdown
CI starts a SQL Server service container so MSSQL-backed CRUD, migration, and seed tests run in pull-request checks instead of being silently skipped.
```

Under `Current Codebase Status`, update the final sentence to mention:

```markdown
The mask endpoint uses a cached compiled matcher that is invalidated after sensitive-word writes, so normal chat requests do not rebuild the active word list on every call.
```

- [x] **Step 2: Verify README contains the review-hardening claims**

Run:

```bash
python3 - <<'PY'
from pathlib import Path
readme = Path("README.md").read_text()
for text in [
    "process liveness only",
    "verifies the API can reach MSSQL",
    "intentionally enabled only for Development",
    "SQL Server service container",
    "cached compiled matcher",
]:
    assert text in readme, text
print("README hardening notes present")
PY
```

Expected: `README hardening notes present`.

- [x] **Step 3: Commit**

Run:

```bash
git add README.md
git commit -m "docs: clarify operational hardening choices"
```

---

### Task 8: Final Verification

**Files:**
- Verify all changed files.

- [x] **Step 1: Run repository checks**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Expected: restore succeeds, build succeeds with `0 Error(s)`, tests pass.

- [x] **Step 2: Validate Docker configuration**

Run:

```bash
MSSQL_SA_PASSWORD=Test_password123 FLASHINTERVIEW_ADMIN_API_KEY=test-key docker compose config --quiet
docker compose -f docker-compose.dev.yml config --quiet
MSSQL_SA_PASSWORD=Test_password123 FLASHINTERVIEW_ADMIN_API_KEY=test-key FLASH_INTERVIEW_API_IMAGE=api:test FLASH_INTERVIEW_WEB_IMAGE=web:test docker compose -f deploy/docker-compose.release.yml config --quiet
```

Expected: all commands exit `0`.

- [x] **Step 3: Inspect git state**

Run:

```bash
git status --short
```

Expected: no unstaged changes. If changes remain, inspect them and either commit the intended files or document why they are intentionally left uncommitted.
