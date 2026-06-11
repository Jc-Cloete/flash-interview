using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace FlashInterview.Infrastructure.Auth;

public static class IdentityResultExtensions
{
    public static IReadOnlyList<AuthWorkflowValidationError> ToAuthWorkflowValidationErrors(this IdentityResult result)
    {
        return result.Errors
            .Select(error => new AuthWorkflowValidationError(string.Empty, error.Description))
            .ToArray();
    }

    public static IReadOnlyList<UserManagementWorkflowValidationError> ToUserManagementWorkflowValidationErrors(
        this IdentityResult result)
    {
        return result.Errors
            .Select(error => new UserManagementWorkflowValidationError(string.Empty, error.Description))
            .ToArray();
    }
}
