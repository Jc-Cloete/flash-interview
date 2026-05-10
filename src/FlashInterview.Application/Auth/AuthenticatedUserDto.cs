namespace FlashInterview.Application.Auth;

public sealed record AuthenticatedUserDto(
    string Id,
    string Email,
    string? DisplayName,
    IReadOnlyCollection<string> Roles);
