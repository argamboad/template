using System.Security.Claims;

namespace Template.Api.Services;

/// <summary>
/// Extracts claims from an authenticated principal, provider-agnostically.
/// </summary>
public class ClaimsExtractor : IClaimsExtractor
{
    public (string? Provider, string? ProviderUserId, string? Email) ExtractClaims(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var providerUserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value
                   ?? principal.FindFirst("preferred_username")?.Value;
        var provider = DetectProvider(principal);

        return (provider, providerUserId, email);
    }

    public string? ExtractDisplayName(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var name = principal.FindFirst(ClaimTypes.Name)?.Value
                   ?? principal.FindFirst("name")?.Value;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public bool IsEmailVerified(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claim = principal.FindFirst("email_verified")?.Value;
        return !string.Equals(claim, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectProvider(ClaimsPrincipal principal)
    {
        // The Google OAuth handler stamps the identity with AuthenticationType "Google".
        if (string.Equals(principal.Identity?.AuthenticationType, "Google", StringComparison.OrdinalIgnoreCase))
            return "google";

        var issuer = principal.FindFirst("iss")?.Value
                     ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Issuer
                     ?? string.Empty;

        if (issuer.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
            return "google";

        // Microsoft issuers vary by tenant (login.microsoftonline.com/{tenant},
        // sts.windows.net, login.live.com) — any authenticated non-Google
        // principal comes from the Microsoft scheme in this app.
        return principal.Identity?.IsAuthenticated == true ? "microsoft" : null;
    }
}
