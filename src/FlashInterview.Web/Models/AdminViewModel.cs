using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Web.Models;

public sealed class AdminViewModel
{
    public IReadOnlyList<SensitiveWordDto> SensitiveWords { get; init; } = [];

    public CreateSensitiveWordRequest NewSensitiveWord { get; init; } = new("", "sql", true);

    public string? ErrorMessage { get; init; }
}
