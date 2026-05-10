using FlashInterview.Api.Security;
using FlashInterview.Application.Auth;
using FlashInterview.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FlashInterview.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize(Policy = AuthorizationPolicies.AdminApiKey)]
public sealed class AuthController(
    UserManager<FlashInterviewUser> userManager,
    SignInManager<FlashInterviewUser> signInManager) : ControllerBase
{
    private const string GoogleProvider = "Google";

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

        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return Unauthorized();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new AuthenticatedUserDto(
            user.Id,
            user.Email ?? email,
            user.DisplayName,
            roles.ToArray()));
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

        if (!request.EmailVerified)
        {
            ModelState.AddModelError(nameof(request.EmailVerified), "The external email address must be verified.");
            return ValidationProblem(ModelState);
        }

        var provider = request.Provider.Trim();
        if (!IsAllowedExternalLoginProvider(provider))
        {
            ModelState.AddModelError(nameof(request.Provider), "Only Google external logins are supported.");
            return ValidationProblem(ModelState);
        }

        var providerKey = request.ProviderKey.Trim();
        var user = await userManager.FindByLoginAsync(provider, providerKey);
        if (user is not null)
        {
            return Ok(await CreateAuthenticatedUserAsync(user));
        }

        var email = request.Email.Trim();
        user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new FlashInterviewUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return ValidationProblem(ModelState);
            }
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return ValidationProblem(ModelState);
            }
        }

        var linkResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(provider, providerKey, provider));
        if (!linkResult.Succeeded)
        {
            foreach (var error in linkResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            await userManager.UpdateAsync(user);
        }

        return Ok(await CreateAuthenticatedUserAsync(user));
    }

    [HttpPost("external/sign-in")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<AuthenticatedUserDto>> ExternalSignInLegacy(
        [FromBody] JsonElement requestBody,
        CancellationToken cancellationToken)
    {
        return ExternalSignIn(requestBody, cancellationToken);
    }

    private static bool IsAllowedExternalLoginProvider(string provider)
    {
        return string.Equals(provider, GoogleProvider, StringComparison.Ordinal);
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

    private async Task<AuthenticatedUserDto> CreateAuthenticatedUserAsync(FlashInterviewUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUserDto(
            user.Id,
            user.Email ?? user.UserName ?? "",
            user.DisplayName,
            roles.ToArray());
    }
}
