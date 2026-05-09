using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Web.Models;

public sealed class AdminViewModel
{
    public IReadOnlyList<SensitiveWordDto> SensitiveWords { get; init; } = [];

    public AdminFilterViewModel Filter { get; init; } = new();

    public CreateSensitiveWordRequest NewSensitiveWord { get; init; } = new("", "sql", true);

    public string? ErrorMessage { get; init; }
}

public sealed class AdminFilterViewModel
{
    public string? Q { get; init; }

    public string? Category { get; init; }

    public bool? IsActive { get; init; }
}
