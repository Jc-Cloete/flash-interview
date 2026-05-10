using FlashInterview.Api.Security;
using FlashInterview.Application.Auth;
using FlashInterview.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.AdminApiKey)]
public sealed class UsersController(
    UserManager<FlashInterviewUser> userManager,
    RoleManager<IdentityRole> roleManager) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "List users", Description = "Lists application users for internal user management.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The user list.", typeof(IReadOnlyList<UserListItemDto>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing or invalid admin API key.")]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> List(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);
        var userDtos = new List<UserListItemDto>(users.Count);

        foreach (var user in users)
        {
            userDtos.Add(await CreateUserListItemAsync(user));
        }

        return Ok(userDtos);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Create a local user", Description = "Creates a local username/password account for admin-managed access.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The created user.", typeof(UserListItemDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation or the user could not be created.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing or invalid admin API key.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "A user already exists for the supplied email.")]
    public async Task<ActionResult<UserListItemDto>> Create([FromBody] CreateUserRequest request)
    {
        if (!TryValidateRequest(request))
        {
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict();
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
            return ValidationProblem(createResult);
        }

        if (request.IsAdmin)
        {
            await EnsureRoleExistsAsync(ApplicationRoles.Admin);
            var roleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
            if (!roleResult.Succeeded)
            {
                return ValidationProblem(roleResult);
            }
        }

        var createdUser = await CreateUserListItemAsync(user);
        return CreatedAtAction(nameof(List), new { id = user.Id }, createdUser);
    }

    [HttpPut("{id}/roles/admin")]
    [SwaggerOperation(Summary = "Update Admin role", Description = "Grants or revokes the Admin role for a user.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The updated user.", typeof(UserListItemDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The role update failed validation.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing or invalid admin API key.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No user exists for the supplied identifier.")]
    public async Task<ActionResult<UserListItemDto>> UpdateAdminRole(
        string id,
        [FromBody] UserRoleUpdateRequest request)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        await EnsureRoleExistsAsync(ApplicationRoles.Admin);
        var isAdmin = await userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        if (request.IsAdmin && !isAdmin)
        {
            var addResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
            if (!addResult.Succeeded)
            {
                return ValidationProblem(addResult);
            }

            var stampResult = await UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                return ValidationProblem(stampResult);
            }
        }
        else if (!request.IsAdmin && isAdmin)
        {
            var removeResult = await userManager.RemoveFromRoleAsync(user, ApplicationRoles.Admin);
            if (!removeResult.Succeeded)
            {
                return ValidationProblem(removeResult);
            }

            var stampResult = await UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                return ValidationProblem(stampResult);
            }
        }

        return Ok(await CreateUserListItemAsync(user));
    }

    private async Task<UserListItemDto> CreateUserListItemAsync(FlashInterviewUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

        return new UserListItemDto(
            user.Id,
            user.Email ?? user.UserName ?? "",
            user.DisplayName,
            isLockedOut,
            roles.Order(StringComparer.Ordinal).ToArray());
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create role '{role}': {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }
    }

    private async Task<IdentityResult> UpdateSecurityStampAsync(FlashInterviewUser user)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        return await userManager.UpdateSecurityStampAsync(user);
    }

    private bool TryValidateRequest<TRequest>(TRequest request)
        where TRequest : notnull
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true))
        {
            return true;
        }

        foreach (var validationResult in validationResults)
        {
            var memberNames = validationResult.MemberNames.Any()
                ? validationResult.MemberNames
                : [string.Empty];

            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(memberName, validationResult.ErrorMessage ?? "The request is invalid.");
            }
        }

        return false;
    }

    private ActionResult ValidationProblem(IdentityResult identityResult)
    {
        foreach (var error in identityResult.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}
