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
    LoadTestOptions options;
    try
    {
        options = LoadTestOptions.Parse(args);
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }

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
    Console.WriteLine("  dotnet run --project tests/FlashInterview.PerformanceTests -- load --base-url http://localhost:7001 --admin-api-key local-dev-admin-key [--smoke|--profile baseline|capacity] [--client-ip-pool-size 100]");
}
