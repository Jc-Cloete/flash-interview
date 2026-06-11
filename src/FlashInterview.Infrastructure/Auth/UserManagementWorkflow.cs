using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Infrastructure.Auth;

public sealed class UserManagementWorkflow(
    UserManager<FlashInterviewUser> userManager,
    RoleManager<IdentityRole> roleManager,
    FlashInterviewDbContext dbContext) : IUserManagementWorkflow
{
    public async Task<UserManagementWorkflowListResult> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);
        var userIds = users.Select(user => user.Id).ToArray();
        var roleRows = await dbContext.UserRoles
            .Where(userRole => userIds.Contains(userRole.UserId))
            .Join(
                dbContext.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);
        var rolesByUserId = roleRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Name!).Order(StringComparer.Ordinal).ToArray());

        var userDtos = users
            .Select(user => CreateUserListItem(
                user,
                rolesByUserId.GetValueOrDefault(user.Id) ?? []))
            .ToArray();

        return new UserManagementWorkflowListResult(userDtos);
    }

    public async Task<UserManagementWorkflowUserResult> CreateAsync(CreateUserRequest request)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return UserManagementWorkflowUserResult.Conflict();
        }

        var user = new FlashInterviewUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return UserManagementWorkflowUserResult.ValidationFailed(createResult.ToUserManagementWorkflowValidationErrors());
        }

        if (request.IsAdmin)
        {
            await EnsureRoleExistsAsync(ApplicationRoles.Admin);
            var roleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
            if (!roleResult.Succeeded)
            {
                return UserManagementWorkflowUserResult.ValidationFailed(
                    await TryDeleteCreatedUserAfterFailureAsync(user, roleResult));
            }
        }

        return UserManagementWorkflowUserResult.Succeeded(await CreateUserListItemAsync(user));
    }

    public async Task<UserManagementWorkflowUserResult> UpdateAdminRoleAsync(
        string id,
        UserRoleUpdateRequest request)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return UserManagementWorkflowUserResult.NotFound();
        }

        await EnsureRoleExistsAsync(ApplicationRoles.Admin);
        var isAdmin = await userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        if (request.IsAdmin && !isAdmin)
        {
            var addResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
            if (!addResult.Succeeded)
            {
                return UserManagementWorkflowUserResult.ValidationFailed(addResult.ToUserManagementWorkflowValidationErrors());
            }

            var stampResult = await UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                var undoResult = await userManager.RemoveFromRoleAsync(user, ApplicationRoles.Admin);
                return UserManagementWorkflowUserResult.ValidationFailed(ToValidationErrors(stampResult, undoResult));
            }
        }
        else if (!request.IsAdmin && isAdmin)
        {
            var removeResult = await userManager.RemoveFromRoleAsync(user, ApplicationRoles.Admin);
            if (!removeResult.Succeeded)
            {
                return UserManagementWorkflowUserResult.ValidationFailed(removeResult.ToUserManagementWorkflowValidationErrors());
            }

            var stampResult = await UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                var undoResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
                return UserManagementWorkflowUserResult.ValidationFailed(ToValidationErrors(stampResult, undoResult));
            }
        }

        return UserManagementWorkflowUserResult.Succeeded(await CreateUserListItemAsync(user));
    }

    private async Task<UserListItemDto> CreateUserListItemAsync(FlashInterviewUser user)
    {
        var roles = await userManager.GetRolesAsync(user);

        return CreateUserListItem(user, roles.Order(StringComparer.Ordinal).ToArray());
    }

    private static UserListItemDto CreateUserListItem(FlashInterviewUser user, IReadOnlyList<string> roles)
    {
        var isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

        return new UserListItemDto(
            user.Id,
            user.Email ?? user.UserName ?? "",
            user.DisplayName,
            isLockedOut,
            roles);
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (await roleManager.RoleExistsAsync(role))
        {
            return;
        }

        IdentityResult result;
        try
        {
            result = await roleManager.CreateAsync(new IdentityRole(role));
        }
        catch (DbUpdateException)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                return;
            }

            throw;
        }

        if (result.Succeeded)
        {
            return;
        }

        if (IsDuplicateRoleCreation(result) && await roleManager.RoleExistsAsync(role))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not create role '{role}': {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }

    private static bool IsDuplicateRoleCreation(IdentityResult result)
    {
        return result.Errors.Any(error =>
            error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
            || error.Description.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || error.Description.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<UserManagementWorkflowValidationError> ToValidationErrors(
        params IdentityResult[] results)
    {
        return results
            .Where(result => !result.Succeeded)
            .SelectMany(result => result.ToUserManagementWorkflowValidationErrors())
            .ToArray();
    }

    private async Task<IReadOnlyList<UserManagementWorkflowValidationError>> TryDeleteCreatedUserAfterFailureAsync(
        FlashInterviewUser user,
        IdentityResult originalFailure)
    {
        var validationErrors = originalFailure.ToUserManagementWorkflowValidationErrors().ToList();
        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            validationErrors.AddRange(deleteResult.ToUserManagementWorkflowValidationErrors());
        }

        return validationErrors;
    }

    private async Task<IdentityResult> UpdateSecurityStampAsync(FlashInterviewUser user)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        return await userManager.UpdateSecurityStampAsync(user);
    }
}
