using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Template.Api.Configuration;

namespace Template.Api.Services;

/// <summary>
/// Issues and validates JWT access tokens for API access.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a JWT access token for the authenticated user. The optional display
    /// name becomes a 'name' claim and the tenant name becomes a tenant_name claim
    /// (both surfaced in the client top bar).
    /// </summary>
    string IssueAccessToken(Guid userId, string email, string provider, string? displayName = null, string? tenantName = null, string? locale = null, Guid? tenantId = null);

    /// <summary>Validates a JWT token and returns its claims if valid.</summary>
    ClaimsPrincipal? ValidateToken(string token);
}

public class JwtTokenService(IJwtSettings settings, TimeProvider clock, ILogger<JwtTokenService> logger) : IJwtTokenService
{
    /// <summary>Alias for <see cref="JwtClaims.TenantId"/>, kept for call-site readability.</summary>
    public const string TenantIdClaim = JwtClaims.TenantId;

    public string IssueAccessToken(Guid userId, string email, string provider, string? displayName = null, string? tenantName = null, string? locale = null, Guid? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty", nameof(provider));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(JwtClaims.Provider, provider),
            // jti is an opaque uniqueness token, not a DB key — random Guid, not UUIDv7.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        // ClaimTypes.Name drives the client's display name; fall back to email.
        claims.Add(new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? email : displayName));
        if (!string.IsNullOrWhiteSpace(tenantName))
            claims.Add(new Claim(JwtClaims.TenantName, tenantName));
        if (!string.IsNullOrWhiteSpace(locale))
            claims.Add(new Claim(JwtClaims.Locale, locale));
        if (tenantId is { } tid)
            claims.Add(new Claim(JwtClaims.TenantId, tid.ToString()));

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Issuer,
            claims: claims,
            expires: clock.GetUtcNow().UtcDateTime.AddMinutes(settings.ExpiryMinutes),
            signingCredentials: credentials
        );

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        logger.LogInformation("JWT issued for user {Email} (id: {UserId})", email, userId);

        return jwt;
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            // Same validation rules as the JWT-bearer handler — both come from JwtValidation.
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, settings.CreateParameters(), out _);

            return principal;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT validation failed");
            return null;
        }
    }
}
