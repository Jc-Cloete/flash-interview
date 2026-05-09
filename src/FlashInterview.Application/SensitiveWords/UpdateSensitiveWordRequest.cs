using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Application.SensitiveWords;

public sealed record UpdateSensitiveWordRequest(
    [Required, NotBlank, StringLength(256, MinimumLength = 1)] string Value,
    [StringLength(64)] string? Category,
    bool IsActive);
