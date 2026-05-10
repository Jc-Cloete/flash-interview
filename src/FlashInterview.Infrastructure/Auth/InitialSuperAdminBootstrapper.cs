using FlashInterview.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashInterview.Infrastructure.Auth;

public sealed class InitialSuperAdminBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<InitialSuperAdminOptions> options,
    ILogger<InitialSuperAdminBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!configured.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configured.Email) || string.IsNullOrWhiteSpace(configured.Password))
        {
            logger.LogWarning("Initial super admin bootstrap is enabled but email or password is missing.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await EnsureRoleAsync(roleManager, ApplicationRoles.Admin);
        await EnsureRoleAsync(roleManager, ApplicationRoles.SuperAdmin);

        var email = configured.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new FlashInterviewUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email
            };

            var createResult = await userManager.CreateAsync(user, configured.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Initial super admin user could not be created: {FormatIdentityErrors(createResult)}");
            }
        }
        else
        {
            user.EmailConfirmed = true;
            user.UserName = email;
            user.Email = email;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Initial super admin user could not be updated: {FormatIdentityErrors(updateResult)}");
            }

            await EnsureConfiguredPasswordAsync(userManager, user, configured.Password);
        }

        await EnsureUserRoleAsync(userManager, user, ApplicationRoles.Admin);
        await EnsureUserRoleAsync(userManager, user, ApplicationRoles.SuperAdmin);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Role '{roleName}' could not be created: {FormatIdentityErrors(result)}");
        }
    }

    private static async Task EnsureUserRoleAsync(
        UserManager<FlashInterviewUser> userManager,
        FlashInterviewUser user,
        string roleName)
    {
        if (await userManager.IsInRoleAsync(user, roleName))
        {
            return;
        }

        var result = await userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Role '{roleName}' could not be assigned: {FormatIdentityErrors(result)}");
        }
    }

    private static async Task EnsureConfiguredPasswordAsync(
        UserManager<FlashInterviewUser> userManager,
        FlashInterviewUser user,
        string configuredPassword)
    {
        if (await userManager.CheckPasswordAsync(user, configuredPassword))
        {
            return;
        }

        IdentityResult result;
        if (await userManager.HasPasswordAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            result = await userManager.ResetPasswordAsync(user, token, configuredPassword);
        }
        else
        {
            result = await userManager.AddPasswordAsync(user, configuredPassword);
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Initial super admin password could not be configured: {FormatIdentityErrors(result)}");
        }
    }

    private static string FormatIdentityErrors(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
    }
}
