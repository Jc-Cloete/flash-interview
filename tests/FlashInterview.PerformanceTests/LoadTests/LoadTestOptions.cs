namespace FlashInterview.PerformanceTests.LoadTests;

public sealed record LoadTestOptions(
    Uri BaseUrl,
    string AdminApiKey,
    bool Smoke,
    string Profile,
    string ReportFolder,
    int ClientIpPoolSize)
{
    public static LoadTestOptions Parse(string[] args)
    {
        var baseUrl = GetValue(args, "--base-url") ?? "http://localhost:7001";
        var adminApiKey = GetValue(args, "--admin-api-key")
            ?? Environment.GetEnvironmentVariable("FLASHINTERVIEW_ADMIN_API_KEY")
            ?? "local-dev-admin-key";
        var reportFolder = GetValue(args, "--report-folder") ?? "artifacts/performance";
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var profile = ParseProfile(GetValue(args, "--profile") ?? "baseline");
        var clientIpPoolSize = ParseClientIpPoolSize(GetValue(args, "--client-ip-pool-size"), smoke, profile);

        return new LoadTestOptions(new Uri(baseUrl), adminApiKey, smoke, profile, reportFolder, clientIpPoolSize);
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                if (index == args.Length - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{name} requires a value.");
                }

                return args[index + 1];
            }
        }

        return null;
    }

    private static string ParseProfile(string profile)
    {
        if (string.Equals(profile, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            return "baseline";
        }

        if (string.Equals(profile, "capacity", StringComparison.OrdinalIgnoreCase))
        {
            return "capacity";
        }

        throw new ArgumentException("--profile must be either 'baseline' or 'capacity'.");
    }

    private static int ParseClientIpPoolSize(string? configuredValue, bool smoke, string profile)
    {
        if (configuredValue is not null)
        {
            if (!int.TryParse(configuredValue, out var parsedValue) || parsedValue < 1)
            {
                throw new ArgumentException("--client-ip-pool-size must be a positive integer.");
            }

            return parsedValue;
        }

        if (smoke)
        {
            return 4;
        }

        return string.Equals(profile, "capacity", StringComparison.OrdinalIgnoreCase) ? 500 : 100;
    }
}
