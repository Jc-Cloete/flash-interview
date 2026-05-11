using FlashInterview.Application.Auth;

namespace FlashInterview.Api.Users;

public interface IUserManagementWorkflow
{
    Task<UserManagementWorkflowListResult> ListAsync(CancellationToken cancellationToken);

    Task<UserManagementWorkflowUserResult> CreateAsync(CreateUserRequest request);

    Task<UserManagementWorkflowUserResult> UpdateAdminRoleAsync(string id, UserRoleUpdateRequest request);
}

public sealed record UserManagementWorkflowListResult(IReadOnlyList<UserListItemDto> Users);

public sealed record UserManagementWorkflowUserResult(
    UserManagementWorkflowStatus Status,
    UserListItemDto? User = null,
    IReadOnlyList<UserManagementWorkflowValidationError>? ValidationErrors = null)
{
    public static UserManagementWorkflowUserResult Succeeded(UserListItemDto user)
    {
        return new UserManagementWorkflowUserResult(UserManagementWorkflowStatus.Succeeded, user);
    }

    public static UserManagementWorkflowUserResult Conflict()
    {
        return new UserManagementWorkflowUserResult(UserManagementWorkflowStatus.Conflict);
    }

    public static UserManagementWorkflowUserResult NotFound()
    {
        return new UserManagementWorkflowUserResult(UserManagementWorkflowStatus.NotFound);
    }

    public static UserManagementWorkflowUserResult ValidationFailed(
        IReadOnlyList<UserManagementWorkflowValidationError> validationErrors)
    {
        return new UserManagementWorkflowUserResult(
            UserManagementWorkflowStatus.ValidationFailed,
            ValidationErrors: validationErrors);
    }
}

public enum UserManagementWorkflowStatus
{
    Succeeded,
    Conflict,
    NotFound,
    ValidationFailed
}

public sealed record UserManagementWorkflowValidationError(string Key, string Message);
