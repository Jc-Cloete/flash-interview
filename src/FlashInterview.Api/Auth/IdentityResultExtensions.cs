using Microsoft.AspNetCore.Identity;

namespace FlashInterview.Api.Auth;

public static class IdentityResultExtensions
{
    public static IReadOnlyList<AuthWorkflowValidationError> ToValidationErrors(this IdentityResult result)
    {
        return result.Errors
            .Select(error => new AuthWorkflowValidationError(string.Empty, error.Description))
            .ToArray();
    }
}
