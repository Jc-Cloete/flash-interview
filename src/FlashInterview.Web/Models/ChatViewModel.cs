using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Web.Models;

public sealed class ChatViewModel
{
    public string Message { get; init; } = "SELECT * FROM sensitiveWords";

    public MaskMessageResponse? Result { get; init; }

    public string? ErrorMessage { get; init; }
}
