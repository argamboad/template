using System.Security.Claims;

namespace Template.Api.Services;

/// <summary>
/// Extracts user identity claims from an authenticated principal.
/// Centralizes claim extraction logic to prevent duplication.
/// </summary>
public interface IClaimsExtractor
{
    /// <summary>
    /// Extracts the OAuth provider, provider user ID, and email from the
    /// claims principal. Provider is detected from the identity's
    /// authentication type or token issuer.
    /// </summary>
    (string? Provider, string? ProviderUserId, string? Email) ExtractClaims(ClaimsPrincipal principal);

    /// <summary>The user's display name from the provider, when present.</summary>
    string? ExtractDisplayName(ClaimsPrincipal principal);

    /// <summary>
    /// False only when the provider explicitly asserts email_verified=false.
    /// An absent claim is trusted (Microsoft/Google only assert verified emails).
    /// Guards the email-match merge against account takeover.
    /// </summary>
    bool IsEmailVerified(ClaimsPrincipal principal);
}
