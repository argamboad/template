using System.Security.Claims;

namespace Perezosoft.Api.Authentication;

/// <summary>
/// Shared accessors for the authenticated principal, so the "current user id from the NameIdentifier
/// claim" parse lives in one tested place instead of being re-implemented in every controller and
/// endpoint (v2 audit DEBT-2).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user id from the <see cref="ClaimTypes.NameIdentifier"/> claim, or null if absent/unparseable.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
