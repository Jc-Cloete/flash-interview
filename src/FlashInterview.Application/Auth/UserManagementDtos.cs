namespace FlashInterview.Application.Auth;

using System.ComponentModel.DataAnnotations;

public sealed record UserListItemDto(
    string Id,
    string Email,
    string? DisplayName,
    bool IsLockedOut,
    IReadOnlyCollection<string> Roles);

public sealed record CreateUserRequest
{
    public CreateUserRequest(string email, string? displayName, string password, bool IsAdmin)
    {
        Email = email;
        DisplayName = displayName;
        Password = password;
        this.IsAdmin = IsAdmin;
    }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; }

    [StringLength(256)]
    public string? DisplayName { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; init; }

    public bool IsAdmin { get; init; }
}

public sealed record UserRoleUpdateRequest(bool IsAdmin);
