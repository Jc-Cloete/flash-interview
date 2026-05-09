using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Application.SensitiveWords;

public sealed record MaskMessageRequest(
    [Required(AllowEmptyStrings = true), StringLength(10000)] string Message);
