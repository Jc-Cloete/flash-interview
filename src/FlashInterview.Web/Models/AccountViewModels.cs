using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Web.Models;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }

    public bool IsGoogleSignInConfigured { get; set; }
}

public sealed class RegisterViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = "";

    [StringLength(256)]
    public string? DisplayName { get; set; }
}
