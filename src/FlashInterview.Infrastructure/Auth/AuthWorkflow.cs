using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace FlashInterview.Infrastructure.Auth;

public sealed class AuthWorkflow(
    UserManager<FlashInterviewUser> userManager,
    SignInManager<FlashInterviewUser> signInManager) : IAuthWorkflow
{
    private const string GoogleProvider = "Google";

    public async Task<AuthWorkflowResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            return AuthWorkflowResult.Unauthorized();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);
        cancellationToken.ThrowIfCancellationRequested();

        if (!signInResult.Succeeded)
        {
            return AuthWorkflowResult.Unauthorized();
        }

        return AuthWorkflowResult.Succeeded(await CreateAuthenticatedUserAsync(user, cancellationToken, email));
    }

    public async Task<AuthWorkflowResult> ExternalSignInAsync(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.EmailVerified)
        {
            return AuthWorkflowResult.ValidationFailed(
            [
                new AuthWorkflowValidationError(
                    nameof(request.EmailVerified),
                    "The external email address must be verified.")
            ]);
        }

        var submittedProvider = request.Provider.Trim();
        if (!IsAllowedExternalLoginProvider(submittedProvider))
        {
            return AuthWorkflowResult.ValidationFailed(
            [
                new AuthWorkflowValidationError(
                    nameof(request.Provider),
                    "Only Google external logins are supported.")
            ]);
        }

        var provider = GoogleProvider;
        var providerKey = request.ProviderKey.Trim();
        var user = await userManager.FindByLoginAsync(provider, providerKey);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is not null)
        {
            return AuthWorkflowResult.Succeeded(await CreateAuthenticatedUserAsync(user, cancellationToken));
        }

        var email = request.Email.Trim();
        user = await userManager.FindByEmailAsync(email);
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            if (!createResult.Succeeded)
            {
                return AuthWorkflowResult.ValidationFailed(createResult.ToAuthWorkflowValidationErrors());
            }
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            cancellationToken.ThrowIfCancellationRequested();
            if (!updateResult.Succeeded)
            {
                return AuthWorkflowResult.ValidationFailed(updateResult.ToAuthWorkflowValidationErrors());
            }
        }

        var linkResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(provider, providerKey, provider));
        cancellationToken.ThrowIfCancellationRequested();
        if (!linkResult.Succeeded)
        {
            return AuthWorkflowResult.ValidationFailed(linkResult.ToAuthWorkflowValidationErrors());
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            user.UpdatedAt = DateTimeOffset.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            cancellationToken.ThrowIfCancellationRequested();
            if (!updateResult.Succeeded)
            {
                return AuthWorkflowResult.ValidationFailed(updateResult.ToAuthWorkflowValidationErrors());
            }
        }

        return AuthWorkflowResult.Succeeded(await CreateAuthenticatedUserAsync(user, cancellationToken));
    }

    private static bool IsAllowedExternalLoginProvider(string provider)
    {
        return string.Equals(provider, GoogleProvider, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AuthenticatedUserDto> CreateAuthenticatedUserAsync(
        FlashInterviewUser user,
        CancellationToken cancellationToken,
        string? fallbackEmail = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = await userManager.GetRolesAsync(user);
        cancellationToken.ThrowIfCancellationRequested();
        return new AuthenticatedUserDto(
            user.Id,
            user.Email ?? fallbackEmail ?? user.UserName ?? "",
            user.DisplayName,
            roles.ToArray());
    }
}
