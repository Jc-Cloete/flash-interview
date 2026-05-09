namespace FlashInterview.Application.SensitiveWords;

public sealed class SensitiveWordMasker
{
    public MaskMessageResult Mask(string message, IEnumerable<SensitiveWordCandidate> sensitiveWords)
    {
        return CompiledSensitiveWordMasker.FromCandidates(sensitiveWords).Mask(message);
    }
}
