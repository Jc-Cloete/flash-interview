namespace FlashInterview.Application.SensitiveWords;

public sealed record SensitiveWordDto(
    Guid Id,
    string Value,
    string NormalizedValue,
    string? Category,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
