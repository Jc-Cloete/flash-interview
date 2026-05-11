using FlashInterview.Api.Users;
using FlashInterview.Api.Security;
using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.AdminApiKey)]
public sealed class UsersController(IUserManagementWorkflow userManagementWorkflow) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "List users", Description = "Lists application users for internal user management.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The user list.", typeof(IReadOnlyList<UserListItemDto>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing or invalid admin API key.")]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> List(CancellationToken cancellationToken)
    {
        var result = await userManagementWorkflow.ListAsync(cancellationToken);
        return Ok(result.Users);
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

        var result = await userManagementWorkflow.CreateAsync(request);
        return MapCreateWorkflowResult(result);
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
        var result = await userManagementWorkflow.UpdateAdminRoleAsync(id, request);
        return MapUserWorkflowResult(result);
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

    private ActionResult<UserListItemDto> MapCreateWorkflowResult(UserManagementWorkflowUserResult result)
    {
        return result.Status switch
        {
            UserManagementWorkflowStatus.Succeeded => Created(
                $"/api/users/{Uri.EscapeDataString((result.User ?? throw new InvalidOperationException("Successful user workflow result did not include a user.")).Id)}",
                result.User),
            UserManagementWorkflowStatus.Conflict => Conflict(),
            UserManagementWorkflowStatus.ValidationFailed => ValidationProblem(AddValidationErrors(result.ValidationErrors)),
            UserManagementWorkflowStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException($"Unsupported user management workflow status '{result.Status}'.")
        };
    }

    private ActionResult<UserListItemDto> MapUserWorkflowResult(UserManagementWorkflowUserResult result)
    {
        return result.Status switch
        {
            UserManagementWorkflowStatus.Succeeded => Ok(result.User ?? throw new InvalidOperationException("Successful user workflow result did not include a user.")),
            UserManagementWorkflowStatus.NotFound => NotFound(),
            UserManagementWorkflowStatus.Conflict => Conflict(),
            UserManagementWorkflowStatus.ValidationFailed => ValidationProblem(AddValidationErrors(result.ValidationErrors)),
            _ => throw new InvalidOperationException($"Unsupported user management workflow status '{result.Status}'.")
        };
    }

    private ModelStateDictionary AddValidationErrors(IReadOnlyList<UserManagementWorkflowValidationError>? validationErrors)
    {
        foreach (var validationError in validationErrors ?? [])
        {
            ModelState.AddModelError(validationError.Key, validationError.Message);
        }

        return ModelState;
    }
}
