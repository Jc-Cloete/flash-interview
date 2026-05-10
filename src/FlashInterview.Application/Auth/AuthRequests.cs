using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Application.Auth;

public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, StringLength(256, MinimumLength = 1)] string Password);

public sealed record ExternalLoginRequest(
    [property: Required, StringLength(64)] string Provider,
    [property: Required, StringLength(256)] string ProviderKey,
    [property: Required, EmailAddress, StringLength(256)] string Email,
    bool EmailVerified,
    [property: StringLength(256)] string? DisplayName);
