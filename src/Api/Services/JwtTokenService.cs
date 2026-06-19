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

public class JwtTokenService(IJwtSettings settings, ILogger<JwtTokenService> logger) : IJwtTokenService
{
    private const string ProviderClaimName = "provider";
    // Must match Template.Shared.Ui.Auth.AppClaims.* (the client reads these claims).
    private const string TenantNameClaim = "tenant_name";
    private const string LocaleClaim = "locale";

    /// <summary>The caller's tenant id. Read server-side by the current-tenant accessor
    /// to drive tenant query scoping; not consumed by the web client.</summary>
    public const string TenantIdClaim = "tenant_id";

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
            new(ProviderClaimName, provider),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        // ClaimTypes.Name drives the client's display name; fall back to email.
        claims.Add(new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? email : displayName));
        if (!string.IsNullOrWhiteSpace(tenantName))
            claims.Add(new Claim(TenantNameClaim, tenantName));
        if (!string.IsNullOrWhiteSpace(locale))
            claims.Add(new Claim(LocaleClaim, locale));
        if (tenantId is { } tid)
            claims.Add(new Claim(TenantIdClaim, tid.ToString()));

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
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
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
            var handler = new JwtSecurityTokenHandler();

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Issuer,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT validation failed");
            return null;
        }
    }
}
