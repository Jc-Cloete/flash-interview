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
