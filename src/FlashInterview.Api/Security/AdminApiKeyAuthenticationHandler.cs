using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FlashInterview.Api.Security;

public sealed class AdminApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminApiKey";
    public const string HeaderName = "X-Admin-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = configuration["Security:AdminApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Admin API key is not configured."));
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var suppliedKeys))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (suppliedKeys.Count != 1 || !ApiKeysMatch(suppliedKeys[0], configuredKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid admin API key."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-api-key"),
            new Claim(ClaimTypes.Name, "Admin API Key")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool ApiKeysMatch(string? suppliedKey, string configuredKey)
    {
        if (suppliedKey is null)
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);

        return suppliedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }
}
