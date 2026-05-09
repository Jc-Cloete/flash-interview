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
