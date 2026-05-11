using FlashInterview.Api.Security;
using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize(Policy = AuthorizationPolicies.AdminApiKey)]
public sealed class AuthController(IAuthWorkflow authWorkflow) : ControllerBase
{
    [HttpPost("login")]
    [SwaggerOperation(Summary = "Validate local credentials", Description = "Validates local username/password credentials for the MVC web frontend.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The authenticated user profile.", typeof(AuthenticatedUserDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing API key or invalid credentials.")]
    public async Task<ActionResult<AuthenticatedUserDto>> Login(
        [FromBody] JsonElement requestBody,
        CancellationToken cancellationToken)
    {
        var request = ReadLoginRequest(requestBody);
        if (request is null)
        {
            return BadRequest();
        }

        if (!TryValidateRequest(request))
        {
            return ValidationProblem(ModelState);
        }

        var result = await authWorkflow.LoginAsync(request);
        return MapWorkflowResult(result);
    }

    [HttpPost("external-login/sign-in")]
    [SwaggerOperation(Summary = "Sign in with an external login", Description = "Signs in an existing Google-linked user, links a verified Google email to an existing local user, or creates a plain user for a verified Google email.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The authenticated user profile.", typeof(AuthenticatedUserDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The request body failed validation.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing API key.")]
    public async Task<ActionResult<AuthenticatedUserDto>> ExternalSignIn(
        [FromBody] JsonElement requestBody,
        CancellationToken cancellationToken)
    {
        var request = ReadRequest<ExternalLoginRequest>(requestBody);
        if (request is null)
        {
            return BadRequest();
        }

        if (!TryValidateRequest(request))
        {
            return ValidationProblem(ModelState);
        }

        var result = await authWorkflow.ExternalSignInAsync(request);
        return MapWorkflowResult(result);
    }

    [HttpPost("external/sign-in")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<AuthenticatedUserDto>> ExternalSignInLegacy(
        [FromBody] JsonElement requestBody,
        CancellationToken cancellationToken)
    {
        return ExternalSignIn(requestBody, cancellationToken);
    }

    private LoginRequest? ReadLoginRequest(JsonElement requestBody)
    {
        return ReadRequest<LoginRequest>(requestBody);
    }

    private static TRequest? ReadRequest<TRequest>(JsonElement requestBody)
    {
        try
        {
            return requestBody.Deserialize<TRequest>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return default;
        }
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

    private ActionResult<AuthenticatedUserDto> MapWorkflowResult(AuthWorkflowResult result)
    {
        return result.Status switch
        {
            AuthWorkflowStatus.Succeeded => Ok(result.User ?? throw new InvalidOperationException("Successful auth workflow result did not include a user.")),
            AuthWorkflowStatus.Unauthorized => Unauthorized(),
            AuthWorkflowStatus.ValidationFailed => ValidationProblem(AddValidationErrors(result.ValidationErrors)),
            _ => throw new InvalidOperationException($"Unsupported auth workflow status '{result.Status}'.")
        };
    }

    private ModelStateDictionary AddValidationErrors(IReadOnlyList<AuthWorkflowValidationError>? validationErrors)
    {
        foreach (var validationError in validationErrors ?? [])
        {
            ModelState.AddModelError(validationError.Key, validationError.Message);
        }

        return ModelState;
    }
}
