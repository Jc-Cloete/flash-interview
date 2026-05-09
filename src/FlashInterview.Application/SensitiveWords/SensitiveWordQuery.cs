namespace FlashInterview.Application.SensitiveWords;

public sealed record SensitiveWordQuery(
    string? Q,
    string? Category,
    bool? IsActive,
    int Page = 1,
    int PageSize = 50);
