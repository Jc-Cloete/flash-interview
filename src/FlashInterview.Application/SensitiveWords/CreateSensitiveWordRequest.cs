using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Application.SensitiveWords;

public sealed record CreateSensitiveWordRequest(
    [Required, NotBlank, StringLength(256, MinimumLength = 1)] string Value,
    [StringLength(64)] string? Category,
    bool IsActive = true);
