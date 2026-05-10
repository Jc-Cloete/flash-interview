using Microsoft.AspNetCore.Identity;

namespace FlashInterview.Infrastructure.Auth;

public sealed class FlashInterviewUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
