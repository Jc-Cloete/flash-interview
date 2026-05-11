namespace FlashInterview.Application.Auth;

public interface IAuthWorkflow
{
    Task<AuthWorkflowResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthWorkflowResult> ExternalSignInAsync(ExternalLoginRequest request, CancellationToken cancellationToken);
}

public sealed record AuthWorkflowResult(
    AuthWorkflowStatus Status,
    AuthenticatedUserDto? User = null,
    IReadOnlyList<AuthWorkflowValidationError>? ValidationErrors = null)
{
    public static AuthWorkflowResult Succeeded(AuthenticatedUserDto user)
    {
        return new AuthWorkflowResult(AuthWorkflowStatus.Succeeded, user);
    }

    public static AuthWorkflowResult Unauthorized()
    {
        return new AuthWorkflowResult(AuthWorkflowStatus.Unauthorized);
    }

    public static AuthWorkflowResult ValidationFailed(IReadOnlyList<AuthWorkflowValidationError> validationErrors)
    {
        return new AuthWorkflowResult(AuthWorkflowStatus.ValidationFailed, ValidationErrors: validationErrors);
    }
}

public enum AuthWorkflowStatus
{
    Succeeded,
    Unauthorized,
    ValidationFailed
}

public sealed record AuthWorkflowValidationError(string Key, string Message);
