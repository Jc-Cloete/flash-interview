using System.Security.Claims;
using FlashInterview.Application.Auth;
using FlashInterview.Web.Clients;
using FlashInterview.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlashInterview.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    AuthApiClient authApiClient,
    IConfiguration configuration,
    ILogger<AccountController> logger) : Controller
{
    private const string ExternalCookieScheme = "FlashInterview.External";

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(CreateLoginViewModel(returnUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.IsGoogleSignInConfigured = IsGoogleSignInConfigured();
            return View(model);
        }

        var user = await authApiClient.LoginAsync(
            new LoginRequest(model.Email, model.Password),
            cancellationToken);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            model.IsGoogleSignInConfigured = IsGoogleSignInConfigured();
            return View(model);
        }

        await SignInUserAsync(user);
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Chat");
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Register(RegisterViewModel model)
    {
        ModelState.AddModelError(string.Empty, "Registration is managed by an administrator.");
        return View(model);
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        if (!IsGoogleSignInConfigured() || !string.Equals(provider, GoogleDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        properties.Items["LoginProvider"] = provider;
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(
        string? returnUrl = null,
        string? remoteError = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            logger.LogWarning("External sign-in returned a remote error");
            ModelState.AddModelError(string.Empty, "External sign-in failed.");
            return View(nameof(Login), CreateLoginViewModel(returnUrl));
        }

        var externalResult = await HttpContext.AuthenticateAsync(ExternalCookieScheme);
        if (!externalResult.Succeeded || externalResult.Principal is null)
        {
            logger.LogWarning(
                "External sign-in callback could not authenticate the external cookie. Succeeded={Succeeded}, Failure={Failure}",
                externalResult.Succeeded,
                externalResult.Failure?.GetType().Name);
            ModelState.AddModelError(string.Empty, "External sign-in failed.");
            return View(nameof(Login), CreateLoginViewModel(returnUrl));
        }

        var provider = externalResult.Properties?.Items.TryGetValue("LoginProvider", out var loginProvider) == true
            ? loginProvider
            : null;
        var providerKey = externalResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = externalResult.Principal.FindFirstValue(ClaimTypes.Email)
            ?? externalResult.Principal.FindFirstValue("email");
        var displayName = externalResult.Principal.FindFirstValue(ClaimTypes.Name);
        var emailVerified = IsTruthyClaimValue(
            externalResult.Principal.FindFirstValue("email_verified")
            ?? externalResult.Principal.FindFirstValue("urn:google:email_verified")
            ?? externalResult.Principal.FindFirstValue("verified_email")
            ?? externalResult.Principal.FindFirstValue("urn:google:verified_email"));

        await HttpContext.SignOutAsync(ExternalCookieScheme);

        if (!IsAllowedExternalLoginProvider(provider) ||
            string.IsNullOrWhiteSpace(providerKey) ||
            string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "External sign-in callback missed required account details. Provider={Provider}, HasProviderKey={HasProviderKey}, HasEmail={HasEmail}, ClaimTypes={ClaimTypes}",
                provider,
                !string.IsNullOrWhiteSpace(providerKey),
                !string.IsNullOrWhiteSpace(email),
                string.Join(",", externalResult.Principal.Claims.Select(claim => claim.Type).Distinct().Order()));
            ModelState.AddModelError(string.Empty, "External sign-in did not return the required account details.");
            return View(nameof(Login), CreateLoginViewModel(returnUrl));
        }

        var googleProvider = provider!;
        var externalLogin = new ExternalLoginRequest(
            googleProvider,
            providerKey,
            email,
            emailVerified,
            displayName);
        if (!emailVerified)
        {
            logger.LogWarning(
                "Google external sign-in did not include a truthy verified-email claim. ClaimTypes={ClaimTypes}",
                string.Join(",", externalResult.Principal.Claims.Select(claim => claim.Type).Distinct().Order()));
            ModelState.AddModelError(string.Empty, "Google must return a verified email address.");
            return View(nameof(Login), CreateLoginViewModel(returnUrl));
        }

        var user = await authApiClient.ExternalSignInAsync(externalLogin, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("External sign-in was rejected by the auth API");
            ModelState.AddModelError(
                string.Empty,
                "External sign-in failed.");
            return View(nameof(Login), CreateLoginViewModel(returnUrl));
        }

        await SignInUserAsync(user);
        return RedirectToLocal(returnUrl);
    }

    private async Task SignInUserAsync(AuthenticatedUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email)
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false
            });
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Chat");
    }

    private LoginViewModel CreateLoginViewModel(string? returnUrl)
    {
        return new LoginViewModel
        {
            ReturnUrl = returnUrl,
            IsGoogleSignInConfigured = IsGoogleSignInConfigured()
        };
    }

    private bool IsGoogleSignInConfigured()
    {
        return !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) &&
               !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);
    }

    private static bool IsAllowedExternalLoginProvider(string? provider)
    {
        return string.Equals(provider, GoogleDefaults.AuthenticationScheme, StringComparison.Ordinal);
    }

    private static bool IsTruthyClaimValue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }
}
