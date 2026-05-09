namespace FlashInterview.Application.SensitiveWords;

public sealed record MaskMessageResult(
    string OriginalMessage,
    string MaskedMessage,
    IReadOnlyList<SensitiveWordMatch> Matches);
