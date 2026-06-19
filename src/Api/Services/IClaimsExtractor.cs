using System.Security.Claims;

namespace Template.Api.Services;

/// <summary>
/// Extracts user identity claims from an authenticated principal.
/// Centralizes claim extraction logic to prevent duplication.
/// </summary>
public interface IClaimsExtractor
{
    /// <summary>
    /// Extracts the provider user ID and email from the claims principal. The provider
    /// itself is known from the callback route, so it isn't derived here.
    /// </summary>
    (string? ProviderUserId, string? Email) ExtractClaims(ClaimsPrincipal principal);

    /// <summary>The user's display name from the provider, when present.</summary>
    string? ExtractDisplayName(ClaimsPrincipal principal);

    /// <summary>
    /// False only when the provider explicitly asserts email_verified=false.
    /// An absent claim is trusted (Microsoft/Google only assert verified emails).
    /// Guards the email-match merge against account takeover.
    /// </summary>
    bool IsEmailVerified(ClaimsPrincipal principal);
}
