namespace FlashInterview.Infrastructure.SensitiveWords;

public sealed class SensitiveWordEntity
{
    public Guid Id { get; set; }

    public required string Value { get; set; }

    public required string NormalizedValue { get; set; }

    public string? Category { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
