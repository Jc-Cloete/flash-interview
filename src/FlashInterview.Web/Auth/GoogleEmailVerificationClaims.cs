using System.Security.Claims;
using System.Text.Json;

namespace FlashInterview.Web.Auth;

public static class GoogleEmailVerificationClaims
{
    private static readonly string[] VerifiedEmailJsonKeys =
    [
        "email_verified",
        "verified_email"
    ];

    public static void AddVerifiedEmailClaim(ClaimsIdentity identity, JsonElement user)
    {
        if (identity.HasClaim(claim =>
                string.Equals(claim.Type, "email_verified", StringComparison.Ordinal) ||
                string.Equals(claim.Type, "verified_email", StringComparison.Ordinal)))
        {
            return;
        }

        foreach (var jsonKey in VerifiedEmailJsonKeys)
        {
            if (!user.TryGetProperty(jsonKey, out var value) || !TryReadBoolean(value, out var isVerified))
            {
                continue;
            }

            identity.AddClaim(new Claim("email_verified", isVerified ? "true" : "false", ClaimValueTypes.Boolean));
            return;
        }
    }

    private static bool TryReadBoolean(JsonElement value, out bool result)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            result = true;
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            result = false;
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out result))
        {
            return true;
        }

        result = false;
        return false;
    }
}
