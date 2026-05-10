namespace FlashInterview.Infrastructure.Auth;

public sealed class InitialSuperAdminOptions
{
    public const string SectionName = "Security:InitialSuperAdmin";

    public bool Enabled { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }
}
