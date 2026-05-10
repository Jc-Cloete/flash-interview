using System.Text.Json;
using System.Text.Json.Serialization;
using FlashInterview.Application.SensitiveWords;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace FlashInterview.PerformanceTests.LoadTests;

public static class FlashInterviewLoadScenarios
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task RunAsync(LoadTestOptions options)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.ReportFolder);
        var reportFileName = $"flash-interview-{options.Profile}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

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
                .WithHeader("X-Forwarded-For", GetSyntheticClientIp(context.InvocationNumber, options.ClientIpPoolSize))
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
            .WithReportFileName(reportFileName)
            .WithReportFormats(ReportFormat.Txt, ReportFormat.Html)
            .WithReportFinalizer(reportData =>
            {
                var reportPath = Path.Combine(options.ReportFolder, $"{reportFileName}.json");
                File.WriteAllText(reportPath, JsonSerializer.Serialize(reportData, ReportJsonOptions));

                return reportData;
            })
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

    private static string GetSyntheticClientIp(long invocationNumber, int clientIpPoolSize)
    {
        var host = Math.Abs(invocationNumber % clientIpPoolSize) + 1;
        var thirdOctet = (host - 1) / 254;
        var fourthOctet = ((host - 1) % 254) + 1;

        return $"198.18.{thirdOctet}.{fourthOctet}";
    }

    private static void ValidateOptions(LoadTestOptions options)
    {
        if (options.ClientIpPoolSize is < 1 or > 64771)
        {
            throw new ArgumentException("--client-ip-pool-size must be between 1 and 64771.");
        }

        if (!options.Smoke && options.ClientIpPoolSize < 10)
        {
            throw new ArgumentException(
                "Baseline and capacity profiles require --client-ip-pool-size of at least 10 to avoid measuring a single throttled client.");
        }
    }
}
