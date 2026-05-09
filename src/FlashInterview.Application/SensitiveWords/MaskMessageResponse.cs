namespace FlashInterview.Application.SensitiveWords;

public sealed record MaskMessageResponse(
    string OriginalMessage,
    string MaskedMessage,
    IReadOnlyList<SensitiveWordMatch> Matches);
