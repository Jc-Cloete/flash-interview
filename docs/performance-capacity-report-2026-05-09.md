# Flash Interview Performance Capacity Report - 2026-05-09

## Executive Summary

The current performance lab shows the application handling the configured capacity ramp cleanly on the local Docker development stack:

- **18,180 total successful HTTP requests** during the capacity run.
- **0 failed requests** across readiness, message masking, and admin listing.
- Masking endpoint averaged **49.83 RPS across a 5-minute ramp to 100 injected RPS**.
- Admin listing averaged **9.97 RPS across a 5-minute ramp to 20 injected RPS**.
- Masking latency stayed low: **p95 3.16 ms**, **p99 4.09 ms**.
- Admin listing was slower but still stable: **p95 9.30 ms**, **p99 10.76 ms**.
- API container CPU averaged **14.88%** and peaked at **26.44%** during sampled load.
- MSSQL CPU averaged **7.18%** and peaked at **21.36%**.

This run did **not** find a saturation point. The tested profile stayed well within the local stack's available CPU and memory.

## Environment

- Date: 2026-05-09
- Host: macOS on Apple Silicon
- Runtime: .NET 10
- Stack: `docker-compose.dev.yml` plus `docker-compose.observability.yml`
- API: `http://localhost:7001`
- Dashboard: `http://localhost:18888`
- Database: SQL Server 2022 container
- Observability: OpenTelemetry exported to Aspire Dashboard

The observability overlay raises the local mask rate limit to avoid measuring the default per-client throttle during capacity tests.

## Artifacts

- NBomber HTML report: `artifacts/performance/capacity-20260509125635/flash-interview-capacity-20260509125635.html`
- NBomber text report: `artifacts/performance/capacity-20260509125635/flash-interview-capacity-20260509125635.txt`
- NBomber JSON report: `artifacts/performance/capacity-20260509125635/flash-interview-capacity-20260509125635.json`
- Docker resource samples: `artifacts/resource-stats/docker-stats-capacity-20260509125635.txt`
- BenchmarkDotNet report: `BenchmarkDotNet.Artifacts/results/FlashInterview.PerformanceTests.MaskerBenchmarks-report-github.md`

## Load Profile

| Scenario | Load Shape | Duration |
| --- | --- | --- |
| `readiness` | Inject 2 RPS | 2 minutes |
| `mask_message` | Ramp inject to 100 RPS | 5 minutes |
| `admin_list_sensitive_words` | Ramp inject to 20 RPS | 5 minutes |

Because the main scenarios ramp up over 5 minutes, the average observed RPS is roughly half of the final injection target. This is expected for a linear ramp.

## Request Results

| Scenario | Requests | Failures | Avg RPS | Mean | p50 | p95 | p99 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `readiness` | 240 | 0 | 2.00 | 4.08 ms | 3.96 ms | 5.67 ms | 6.28 ms | 25.60 ms |
| `mask_message` | 14,950 | 0 | 49.83 | 1.93 ms | 1.88 ms | 3.16 ms | 4.09 ms | 62.54 ms |
| `admin_list_sensitive_words` | 2,990 | 0 | 9.97 | 6.80 ms | 6.98 ms | 9.30 ms | 10.76 ms | 30.68 ms |

## Payload Volume

| Scenario | Mean Response Size | Total Transfer |
| --- | ---: | ---: |
| `readiness` | 0.341 KB | 0.080 MB |
| `mask_message` | 0.649 KB | 9.482 MB |
| `admin_list_sensitive_words` | 11.207 KB | 32.724 MB |

The admin list endpoint is the heavier payload path by a large margin. Even at one fifth of the masking load target, it transferred more than 3x the total data.

## Container Resource Samples

Resource sampling was captured every 10 seconds during the run.

| Container | Avg CPU | Max CPU | Avg Memory | Max Memory |
| --- | ---: | ---: | ---: | ---: |
| API | 14.88% | 26.44% | 542.6 MiB | 570.2 MiB |
| MSSQL | 7.18% | 21.36% | 1,312.4 MiB | 1,332.2 MiB |
| Web | 2.93% | 9.66% | 267.5 MiB | 272.1 MiB |
| Aspire Dashboard | 0.75% | 4.22% | 145.2 MiB | 145.8 MiB |

The API and SQL Server both had substantial CPU headroom. Memory did not show a growth trend during the sampled window.

## Masking Microbenchmark

BenchmarkDotNet measured the in-process masking engine separately from HTTP, JSON, rate limiting, and database behavior.

| Benchmark | Mean | StdDev | Allocated |
| --- | ---: | ---: | ---: |
| `MaskShortMessage` | 501.2 ns | 12.39 ns | 2.79 KB |

The pure masking operation is not the bottleneck for short messages. End-to-end request time is dominated by web/API pipeline overhead, serialization, cache access, and endpoint behavior.

## Database Interpretation

The capacity run included two relevant paths:

- `mask_message`: uses the cached compiled matcher after active words have been loaded. Under steady state, this should not query MSSQL per request.
- `admin_list_sensitive_words`: queries MSSQL and returns a larger payload.

The admin endpoint p95 of **9.30 ms** and MSSQL average CPU of **7.18%** suggest the database is not currently the limiting factor for this profile. The admin list endpoint is still the first place to watch for payload growth, pagination misuse, or inefficient filtering as data volume increases.

## Capacity Reading

For the tested local server profile, the app handled:

- A ramp to **100 injected mask requests/sec**.
- A ramp to **20 injected admin list requests/sec**.
- No HTTP failures.
- Low p99 latencies on both business endpoints.
- No obvious CPU or memory saturation.

This means the current implementation has clear headroom beyond the tested average load. It does **not** establish the maximum capacity yet, because the run did not push until latency degraded or failures appeared.

As a rough user-facing translation:

- If an active chat user sends **1 message/sec**, the tested average masking throughput corresponds to about **50 active message-sending users** during the ramp average.
- If an active chat user sends **1 message every 10 seconds**, that same average corresponds to about **500 active chat users**.
- Since the ramp ended at a higher target than the average and did not fail, these are baseline demonstrated numbers, not the upper bound.

## Findings

1. **No saturation observed.** The profile completed successfully with low latency and low resource pressure.
2. **Masking is cheap.** The compiled matcher is sub-microsecond for the short benchmark.
3. **Admin listing is the heavier endpoint.** It has larger responses and higher latency than masking.
4. **The database is not currently stressed.** MSSQL CPU remained modest during admin list load.
5. **The API has headroom.** Peak sampled API CPU was 26.44%, with p99 mask latency still near 4 ms.
6. **Dashboard instrumentation is ready for tuning.** The Aspire Dashboard can now be used during future runs to inspect traces, runtime counters, EF spans, and custom masking metrics.

## Limitations

- This was a local Docker development stack, not a production-sized server.
- The API and Web containers run `dotnet watch`, so absolute numbers are not production-container numbers.
- The capacity run did not continue until failure or degradation.
- The report uses NBomber aggregate statistics, not per-second time-series analysis.
- Aspire Dashboard data was available during the run, but this report uses persisted NBomber and Docker stats artifacts as its source of record.

## Recommended Next Runs

1. **Find the real ceiling.** Add or run a higher ramp profile until p95/p99 latency bends upward or failures appear.
2. **Split endpoint tests.** Run masking-only and admin-only profiles to isolate bottlenecks.
3. **Run production-shaped containers.** Repeat against `docker-compose.yml` or release images without `dotnet watch`.
4. **Increase data volume.** Seed more sensitive words and admin-list pages to expose database and payload scaling behavior.
5. **Capture dashboard screenshots or exported traces.** Use Aspire trace waterfalls to confirm where endpoint time is spent.
