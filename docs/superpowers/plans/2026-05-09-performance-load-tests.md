# Performance Observability And Load Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in performance lab that shows high-level capacity, request latency, application metrics, traces, and database timing evidence for later optimization work.

**Architecture:** Instrument the API and MVC web app with OpenTelemetry and export traces/metrics/logs to a local OTLP dashboard when enabled. Add focused API masking metrics for request volume, masking latency, message size, and match count. Add a separate performance console project that runs BenchmarkDotNet microbenchmarks and NBomber HTTP load profiles against the running Docker Compose stack, writing reports under ignored artifacts.

**Tech Stack:** .NET 10, OpenTelemetry, OTLP exporter, Aspire Dashboard container, BenchmarkDotNet, NBomber, ASP.NET Core API/MVC, EF Core SQL Server, Docker Compose.

---

## Scope

This plan builds measurement and visibility, not performance optimizations. Optimization changes should be planned after baseline results identify bottlenecks.

Normal contributor verification remains fast:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

The performance lab is opt-in:

```bash
docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --smoke
dotnet run --project tests/FlashInterview.PerformanceTests -- benchmark --filter "*MaskShortMessage*" --job short
```

## File Structure

- Modify: `src/FlashInterview.Api/FlashInterview.Api.csproj`
  - Add OpenTelemetry package references.
- Modify: `src/FlashInterview.Api/Program.cs`
  - Configure OpenTelemetry resource name, ASP.NET Core metrics/traces, HTTP client traces, EF Core traces, runtime metrics, and optional OTLP export.
- Create: `src/FlashInterview.Api/Telemetry/MaskingMetrics.cs`
  - Business-level meters for mask request count, duration, message length, and match count.
- Modify: `src/FlashInterview.Api/Controllers/MessagesController.cs`
  - Record masking metrics without logging raw message bodies.
- Modify: `src/FlashInterview.Web/FlashInterview.Web.csproj`
  - Add OpenTelemetry package references for MVC and outbound API calls.
- Modify: `src/FlashInterview.Web/Program.cs`
  - Configure OpenTelemetry resource name, ASP.NET Core metrics/traces, HTTP client traces, runtime metrics, and optional OTLP export.
- Create: `docker-compose.observability.yml`
  - Add Aspire Dashboard and OTLP environment variables for API/Web.
- Create: `tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj`
  - Console project for opt-in performance and load runs.
- Create: `tests/FlashInterview.PerformanceTests/Program.cs`
  - Command router for `benchmark` and `load`.
- Create: `tests/FlashInterview.PerformanceTests/MaskerBenchmarks.cs`
  - BenchmarkDotNet scenarios for the compiled masking engine.
- Create: `tests/FlashInterview.PerformanceTests/LoadTests/LoadTestOptions.cs`
  - CLI options for base URL, admin key, smoke mode, target profile, and report folder.
- Create: `tests/FlashInterview.PerformanceTests/LoadTests/FlashInterviewLoadScenarios.cs`
  - NBomber scenarios for masking, admin listing, and readiness.
- Modify: `FlashInterview.slnx`
  - Add the performance project so restore/build covers it.
- Modify: `.gitignore`
  - Ignore local benchmark/load report artifacts.
- Modify: `README.md`
  - Document dashboard URLs, metrics/traces to inspect, load profiles, and report locations.

---

### Task 1: Add OpenTelemetry Dashboard Instrumentation

**Files:**
- Modify: `src/FlashInterview.Api/FlashInterview.Api.csproj`
- Modify: `src/FlashInterview.Api/Program.cs`
- Modify: `src/FlashInterview.Web/FlashInterview.Web.csproj`
- Modify: `src/FlashInterview.Web/Program.cs`
- Create: `docker-compose.observability.yml`

- [x] **Step 1: Add OpenTelemetry packages**

Run:

```bash
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Extensions.Hosting
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Instrumentation.Http
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Instrumentation.EntityFrameworkCore
dotnet add src/FlashInterview.Api/FlashInterview.Api.csproj package OpenTelemetry.Instrumentation.Runtime
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj package OpenTelemetry.Extensions.Hosting
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj package OpenTelemetry.Instrumentation.Http
dotnet add src/FlashInterview.Web/FlashInterview.Web.csproj package OpenTelemetry.Instrumentation.Runtime
```

Expected: package references are added to API and Web projects.

- [x] **Step 2: Configure API OpenTelemetry**

In `src/FlashInterview.Api/Program.cs`, add these usings:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
```

After Serilog setup and before infrastructure registration, add:

```csharp
var apiServiceName = builder.Configuration.GetValue("OpenTelemetry:ServiceName", "FlashInterview.Api");
var apiOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(apiServiceName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("FlashInterview.Api.Masking")
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http");

        if (!string.IsNullOrWhiteSpace(apiOtlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(apiOtlpEndpoint));
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/healthz")
                    && !httpContext.Request.Path.StartsWithSegments("/readyz");
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
            });

        if (!string.IsNullOrWhiteSpace(apiOtlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(apiOtlpEndpoint));
        }
    });
```

Expected: the API emits ASP.NET request metrics, Kestrel metrics, runtime metrics, HTTP client traces, EF Core database spans, and custom masking meters when an OTLP endpoint is configured.

- [x] **Step 3: Configure Web OpenTelemetry**

In `src/FlashInterview.Web/Program.cs`, add these usings:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
```

After Serilog setup and before MVC/client registrations, add:

```csharp
var webServiceName = builder.Configuration.GetValue("OpenTelemetry:ServiceName", "FlashInterview.Web");
var webOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(webServiceName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http");

        if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(webOtlpEndpoint));
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/healthz");
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(webOtlpEndpoint));
        }
    });
```

Expected: MVC request timing and Web-to-API HTTP calls appear in the dashboard when OTLP is enabled.

- [x] **Step 4: Add observability compose overlay**

Create `docker-compose.observability.yml`:

```yaml
services:
  aspire-dashboard:
    image: mcr.microsoft.com/dotnet/aspire-dashboard:latest
    environment:
      DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"
    ports:
      - "18888:18888"
      - "18889:18889"

  api:
    environment:
      OpenTelemetry__ServiceName: "FlashInterview.Api"
      OpenTelemetry__OtlpEndpoint: "http://aspire-dashboard:18889"
    depends_on:
      aspire-dashboard:
        condition: service_started

  web:
    environment:
      OpenTelemetry__ServiceName: "FlashInterview.Web"
      OpenTelemetry__OtlpEndpoint: "http://aspire-dashboard:18889"
    depends_on:
      aspire-dashboard:
        condition: service_started
```

Expected: running `docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build` starts the dashboard on `http://localhost:18888` and gives API/Web an OTLP endpoint.

- [x] **Step 5: Verify build**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
```

Expected: PASS.

- [x] **Step 6: Commit**

Run:

```bash
git add src/FlashInterview.Api src/FlashInterview.Web docker-compose.observability.yml
git commit -m "feat: add open telemetry dashboard instrumentation"
```

---

### Task 2: Add Masking Business Metrics

**Files:**
- Create: `src/FlashInterview.Api/Telemetry/MaskingMetrics.cs`
- Modify: `src/FlashInterview.Api/Program.cs`
- Modify: `src/FlashInterview.Api/Controllers/MessagesController.cs`
- Test: `tests/FlashInterview.Tests/ApiSurfaceTests.cs`

- [x] **Step 1: Add a focused API test for metric-safe masking**

Add this test to `tests/FlashInterview.Tests/ApiSurfaceTests.cs`:

```csharp
[Fact]
public async Task MaskEndpoint_ReturnsMaskedMessage_WhenMetricsAreRegistered()
{
    var repository = new FakeSensitiveWordRepository(
        new SensitiveWordDto(Guid.NewGuid(), "SELECT", "select", "sql", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    using var factory = new FlashInterviewApiFactory(repository);
    using var client = factory.CreateHttpsClient();

    using var response = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("SELECT value"));

    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<MaskMessageResponse>();

    Assert.NotNull(result);
    Assert.Equal("****** value", result.MaskedMessage);
}
```

Expected: this passes before metrics are added and remains a guard that adding metrics does not change API behavior.

- [x] **Step 2: Add masking metric recorder**

Create `src/FlashInterview.Api/Telemetry/MaskingMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.Telemetry;

public sealed class MaskingMetrics
{
    public const string MeterName = "FlashInterview.Api.Masking";

    private readonly Counter<long> requests;
    private readonly Histogram<double> duration;
    private readonly Histogram<int> messageLength;
    private readonly Histogram<int> matchCount;

    public MaskingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        requests = meter.CreateCounter<long>(
            "flashinterview.mask.requests",
            description: "Number of mask requests processed by the API.");
        duration = meter.CreateHistogram<double>(
            "flashinterview.mask.duration",
            unit: "ms",
            description: "Time spent applying the compiled sensitive-word matcher.");
        messageLength = meter.CreateHistogram<int>(
            "flashinterview.mask.message.length",
            unit: "characters",
            description: "Length of submitted messages. Raw message bodies are not recorded.");
        matchCount = meter.CreateHistogram<int>(
            "flashinterview.mask.matches",
            unit: "matches",
            description: "Number of sensitive-word matches returned for each mask request.");
    }

    public void Record(MaskMessageResult result, TimeSpan elapsed)
    {
        requests.Add(1);
        duration.Record(elapsed.TotalMilliseconds);
        messageLength.Record(result.OriginalMessage.Length);
        matchCount.Record(result.Matches.Count);
    }
}
```

- [x] **Step 3: Register and use masking metrics**

In `src/FlashInterview.Api/Program.cs`, add:

```csharp
using FlashInterview.Api.Telemetry;
```

Register the singleton before controllers:

```csharp
builder.Services.AddSingleton<MaskingMetrics>();
```

Replace the hardcoded meter name:

```csharp
.AddMeter("FlashInterview.Api.Masking")
```

with:

```csharp
.AddMeter(MaskingMetrics.MeterName)
```

Update `MessagesController` constructor:

```csharp
public sealed class MessagesController(
    ISensitiveWordMatcherCache matcherCache,
    MaskingMetrics maskingMetrics) : ControllerBase
```

In `Mask`, wrap the masking call:

```csharp
var startedAt = TimeProvider.System.GetTimestamp();
var result = matcher.Mask(request.Message);
var elapsed = TimeProvider.System.GetElapsedTime(startedAt);
maskingMetrics.Record(result, elapsed);
```

Expected: metrics record dimensions that are safe for privacy. Do not record raw message text.

- [x] **Step 4: Verify focused API test**

Run:

```bash
dotnet test tests/FlashInterview.Tests/FlashInterview.Tests.csproj --filter "FullyQualifiedName~MaskEndpoint_ReturnsMaskedMessage_WhenMetricsAreRegistered"
```

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/FlashInterview.Api tests/FlashInterview.Tests/ApiSurfaceTests.cs
git commit -m "feat: add masking performance metrics"
```

---

### Task 3: Add Performance Test Project And Benchmarks

**Files:**
- Create: `tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj`
- Create: `tests/FlashInterview.PerformanceTests/Program.cs`
- Create: `tests/FlashInterview.PerformanceTests/MaskerBenchmarks.cs`
- Modify: `FlashInterview.slnx`
- Modify: `.gitignore`

- [x] **Step 1: Scaffold the console project**

Run:

```bash
dotnet new console -n FlashInterview.PerformanceTests -o tests/FlashInterview.PerformanceTests --framework net10.0
dotnet sln FlashInterview.slnx add tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj
dotnet add tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj reference src/FlashInterview.Application/FlashInterview.Application.csproj
dotnet add tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj package BenchmarkDotNet
dotnet add tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj package NBomber
dotnet add tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj package NBomber.Http
```

Expected: the project is created, added to the solution, references the application project, and package references are added.

- [x] **Step 2: Add generated artifact ignores**

Add these lines to `.gitignore` if they are not already present:

```gitignore
artifacts/performance/
BenchmarkDotNet.Artifacts/
```

- [x] **Step 3: Add the command router**

Replace `tests/FlashInterview.PerformanceTests/Program.cs` with:

```csharp
using BenchmarkDotNet.Running;
using FlashInterview.PerformanceTests;
using FlashInterview.PerformanceTests.LoadTests;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var command = args[0].Trim().ToLowerInvariant();
var commandArgs = args[1..];

return command switch
{
    "benchmark" => RunBenchmarks(commandArgs),
    "load" => await RunLoadTests(commandArgs),
    _ => UnknownCommand(command)
};

static int RunBenchmarks(string[] args)
{
    BenchmarkSwitcher.FromAssembly(typeof(MaskerBenchmarks).Assembly).Run(args);
    return 0;
}

static async Task<int> RunLoadTests(string[] args)
{
    var options = LoadTestOptions.Parse(args);
    await FlashInterviewLoadScenarios.RunAsync(options);
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tests/FlashInterview.PerformanceTests -- benchmark [BenchmarkDotNet args]");
    Console.WriteLine("  dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key [--smoke|--profile baseline|capacity]");
}
```

Expected: this does not compile until Task 4 adds load scenario types.

- [x] **Step 4: Add masking benchmark class**

Create `tests/FlashInterview.PerformanceTests/MaskerBenchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.PerformanceTests;

[MemoryDiagnoser]
public class MaskerBenchmarks
{
    private readonly CompiledSensitiveWordMasker matcher;
    private readonly string shortMessage;
    private readonly string longMessage;

    public MaskerBenchmarks()
    {
        var candidates = new[]
        {
            new SensitiveWordCandidate("SELECT"),
            new SensitiveWordCandidate("DROP"),
            new SensitiveWordCandidate("DELETE"),
            new SensitiveWordCandidate("FROM"),
            new SensitiveWordCandidate("WHERE"),
            new SensitiveWordCandidate("SELECT * FROM"),
            new SensitiveWordCandidate("INSERT INTO"),
            new SensitiveWordCandidate("ORDER BY")
        };

        matcher = CompiledSensitiveWordMasker.FromCandidates(candidates);
        shortMessage = "SELECT * FROM customers WHERE name = 'Ada'";
        longMessage = string.Join(
            ' ',
            Enumerable.Repeat("Please review this SELECT * FROM customers query before we DROP anything.", 200));
    }

    [Benchmark(Baseline = true)]
    public MaskMessageResult MaskShortMessage()
    {
        return matcher.Mask(shortMessage);
    }

    [Benchmark]
    public MaskMessageResult MaskLongMessage()
    {
        return matcher.Mask(longMessage);
    }
}
```

- [x] **Step 5: Verify benchmark command reaches expected missing load types**

Run:

```bash
dotnet build tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj
```

Expected: FAIL only because `LoadTestOptions` and `FlashInterviewLoadScenarios` do not exist yet.

- [x] **Step 6: Commit**

Run:

```bash
git add FlashInterview.slnx .gitignore tests/FlashInterview.PerformanceTests
git commit -m "test: add performance benchmark project"
```

---

### Task 4: Add NBomber Load Scenarios And Capacity Profiles

**Files:**
- Create: `tests/FlashInterview.PerformanceTests/LoadTests/LoadTestOptions.cs`
- Create: `tests/FlashInterview.PerformanceTests/LoadTests/FlashInterviewLoadScenarios.cs`

- [x] **Step 1: Add load test options**

Create `tests/FlashInterview.PerformanceTests/LoadTests/LoadTestOptions.cs`:

```csharp
namespace FlashInterview.PerformanceTests.LoadTests;

public sealed record LoadTestOptions(
    Uri BaseUrl,
    string AdminApiKey,
    bool Smoke,
    string Profile,
    string ReportFolder)
{
    public static LoadTestOptions Parse(string[] args)
    {
        var baseUrl = GetValue(args, "--base-url") ?? "http://localhost:7001";
        var adminApiKey = GetValue(args, "--admin-api-key")
            ?? Environment.GetEnvironmentVariable("FLASHINTERVIEW_ADMIN_API_KEY")
            ?? "local-dev-admin-key";
        var reportFolder = GetValue(args, "--report-folder") ?? "artifacts/performance";
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var profile = GetValue(args, "--profile") ?? "baseline";

        return new LoadTestOptions(new Uri(baseUrl), adminApiKey, smoke, profile, reportFolder);
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
```

- [x] **Step 2: Add NBomber scenarios**

Create `tests/FlashInterview.PerformanceTests/LoadTests/FlashInterviewLoadScenarios.cs`:

```csharp
using System.Text.Json;
using FlashInterview.Application.SensitiveWords;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace FlashInterview.PerformanceTests.LoadTests;

public static class FlashInterviewLoadScenarios
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task RunAsync(LoadTestOptions options)
    {
        Directory.CreateDirectory(options.ReportFolder);

        using var httpClient = new HttpClient
        {
            BaseAddress = options.BaseUrl,
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Add("X-Admin-Api-Key", options.AdminApiKey);

        var readinessScenario = Scenario.Create("readiness", async _ =>
        {
            var request = Http.CreateRequest("GET", "/readyz");
            return await Http.Send(httpClient, request);
        })
        .WithLoadSimulations(GetReadinessLoad(options));

        var maskScenario = Scenario.Create("mask_message", async context =>
        {
            var message = context.InvocationNumber % 2 == 0
                ? "SELECT * FROM customers WHERE name = 'Ada'"
                : "This normal chat message should avoid sensitive SQL terms.";

            var request = Http.CreateRequest("POST", "/api/messages/mask")
                .WithJsonBody(new MaskMessageRequest(message), JsonOptions);

            return await Http.Send(httpClient, request);
        })
        .WithLoadSimulations(GetMaskLoad(options));

        var adminListScenario = Scenario.Create("admin_list_sensitive_words", async _ =>
        {
            var request = Http.CreateRequest("GET", "/api/sensitive-words?page=1&pageSize=50");
            return await Http.Send(httpClient, request);
        })
        .WithLoadSimulations(GetAdminLoad(options));

        NBomberRunner
            .RegisterScenarios(readinessScenario, maskScenario, adminListScenario)
            .WithReportFolder(options.ReportFolder)
            .WithReportFileName($"flash-interview-{options.Profile}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}")
            .WithReportFormats(ReportFormat.Txt, ReportFormat.Html, ReportFormat.Json)
            .Run();

        return Task.CompletedTask;
    }

    private static LoadSimulation[] GetReadinessLoad(LoadTestOptions options)
    {
        return options.Smoke
            ? [Simulation.Inject(rate: 1, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))]
            : [Simulation.Inject(rate: 2, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2))];
    }

    private static LoadSimulation[] GetMaskLoad(LoadTestOptions options)
    {
        if (options.Smoke)
        {
            return [Simulation.Inject(rate: 2, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))];
        }

        return string.Equals(options.Profile, "capacity", StringComparison.OrdinalIgnoreCase)
            ? [Simulation.RampingInject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(5))]
            : [Simulation.Inject(rate: 25, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2))];
    }

    private static LoadSimulation[] GetAdminLoad(LoadTestOptions options)
    {
        if (options.Smoke)
        {
            return [Simulation.Inject(rate: 1, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))];
        }

        return string.Equals(options.Profile, "capacity", StringComparison.OrdinalIgnoreCase)
            ? [Simulation.RampingInject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(5))]
            : [Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2))];
    }
}
```

- [x] **Step 3: Verify compile and fix package API drift**

Run:

```bash
dotnet build tests/FlashInterview.PerformanceTests/FlashInterview.PerformanceTests.csproj
```

Expected: PASS. If NBomber or NBomber.Http APIs differ from the snippets above, inspect compiler errors and adjust only files under `tests/FlashInterview.PerformanceTests`.

- [x] **Step 4: Run benchmark smoke**

Run:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- benchmark --filter "*MaskShortMessage*" --job short
```

Expected: PASS with BenchmarkDotNet output and artifacts ignored by git.

- [x] **Step 5: Run load smoke when API is available**

Start the stack if needed:

```bash
docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build
```

Then run:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --smoke
```

Expected: PASS with NBomber output and report files under `artifacts/performance/`.

- [x] **Step 6: Commit**

Run:

```bash
git add tests/FlashInterview.PerformanceTests
git commit -m "test: add api load and capacity profiles"
```

---

### Task 5: Document Dashboard And Performance Workflow

**Files:**
- Modify: `README.md`

- [x] **Step 1: Add README performance lab section**

Add this section after the Local Development test commands:

```markdown
## Performance Lab

The performance lab is opt-in. It combines live observability with repeatable load reports:

- Aspire Dashboard receives OpenTelemetry metrics and traces from the API and MVC web app.
- API traces include ASP.NET request spans and EF Core database spans so database timing is visible.
- API metrics include request rates, request duration, runtime counters, HTTP client metrics, and masking-specific counters/histograms.
- BenchmarkDotNet measures the in-process masking engine without HTTP or SQL Server noise.
- NBomber drives HTTP load against the running API and writes capacity/latency reports.

Start the development stack with observability:

```bash
docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build
```

Open the dashboard:

- Aspire Dashboard: `http://localhost:18888`
- API: `http://localhost:7001`
- Web: `http://localhost:7002`

Run masking microbenchmarks:

```bash
dotnet run --project tests/FlashInterview.PerformanceTests -- benchmark
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

Use NBomber reports for high-level throughput, request latency, percentiles, success rate, and saturation points. Use the Aspire Dashboard during the same run to inspect API request metrics, trace waterfalls, MVC-to-API calls, EF Core database spans, runtime counters, and masking-specific histograms.

Reports are written to `artifacts/performance/`. BenchmarkDotNet writes to `BenchmarkDotNet.Artifacts/`. Both folders are local artifacts and are not committed.
```

- [x] **Step 2: Verify README command references**

Run:

```bash
rg "Performance Lab|Aspire Dashboard|OpenTelemetry|FlashInterview.PerformanceTests|artifacts/performance|BenchmarkDotNet|NBomber" README.md .gitignore docker-compose.observability.yml tests/FlashInterview.PerformanceTests src/FlashInterview.Api src/FlashInterview.Web
```

Expected: output shows README guidance, ignore rules, observability compose config, telemetry setup, and performance project files.

- [x] **Step 3: Run full repository verification**

Run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
```

Expected: restore, build, and tests pass. Optional MSSQL-backed tests may skip locally if SQL Server is unavailable.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md
git commit -m "docs: document performance lab workflow"
```

---

## Final Verification

After all tasks are complete, run:

```bash
dotnet restore FlashInterview.slnx
dotnet build FlashInterview.slnx --no-restore
dotnet test FlashInterview.slnx --no-build
dotnet run --project tests/FlashInterview.PerformanceTests -- benchmark --filter "*MaskShortMessage*" --job short
```

If Docker is available and the development stack starts successfully, also run:

```bash
docker compose -f docker-compose.dev.yml -f docker-compose.observability.yml up --build
dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key --smoke
```

Do not claim the dashboard or load smoke passes unless the stack was actually running, Aspire Dashboard was reachable, and NBomber completed successfully.
