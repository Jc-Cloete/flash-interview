using System.ComponentModel.DataAnnotations;
using FlashInterview.Application.Auth;

namespace FlashInterview.Web.Models;

public sealed class UserManagementIndexViewModel
{
    public IReadOnlyList<UserManagementUserViewModel> Users { get; set; } = [];
}

public sealed class UserManagementUserViewModel
{
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public bool IsLockedOut { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsSuperAdmin { get; set; }

    public string RoleSummary { get; set; } = "";

    public static UserManagementUserViewModel FromDto(UserListItemDto user)
    {
        return new UserManagementUserViewModel
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsLockedOut = user.IsLockedOut,
            IsAdmin = user.Roles.Contains(ApplicationRoles.Admin),
            IsSuperAdmin = user.Roles.Contains(ApplicationRoles.SuperAdmin),
            RoleSummary = user.Roles.Count == 0 ? "None" : string.Join(", ", user.Roles.Order(StringComparer.Ordinal))
        };
    }
}

public sealed class UserManagementCreateViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = "";

    [StringLength(256)]
    public string? DisplayName { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; set; } = "";

    public bool IsAdmin { get; set; }
}

public sealed class UserManagementEditViewModel
{
    [Required]
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public bool IsLockedOut { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsSuperAdmin { get; set; }

    public string RoleSummary { get; set; } = "";

    public static UserManagementEditViewModel FromUser(UserManagementUserViewModel user)
    {
        return new UserManagementEditViewModel
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsLockedOut = user.IsLockedOut,
            IsAdmin = user.IsAdmin,
            IsSuperAdmin = user.IsSuperAdmin,
            RoleSummary = user.RoleSummary
        };
    }
}

public sealed class UserManagementRoleUpdateViewModel
{
    [Required]
    public string Id { get; set; } = "";

    public bool IsAdmin { get; set; }
}
