using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Configuration;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure;

namespace Template.Api.Controllers;

/// <summary>
/// Authentication controller for the OAuth → JWT + refresh-token flow.
/// Focused solely on HTTP request/response handling; delegates to services.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    IUserService userService,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ICookieService cookieService,
    IClaimsExtractor claimsExtractor,
    IPasswordlessService passwordless,
    ILinkTokenService linkTokenService,
    IUserLoginRepository userLoginRepository,
    ITenantRepository tenantRepository,
    IEmailSender emailSender,
    IErrorResponseFactory errorFactory,
    IJwtSettings jwtSettings,
    IApplicationSettings appSettings,
    IPasswordlessSettings passwordlessSettings,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly string[] SupportedProviders = ["microsoft", "google"];

    /// <summary>
    /// Starts the OAuth flow: challenges the matching scheme. The callback route
    /// carries the provider so the callback can resolve the right identity.
    /// </summary>
    [HttpGet("login/{provider}")]
    public IActionResult Login(string provider, [FromQuery(Name = "link_token")] string? linkToken = null)
    {
        provider = provider.ToLowerInvariant();
        // A link token (issued to an already-signed-in user) rides through the
        // round-trip so the callback can attach the identity instead of signing in.
        var redirectUri = string.IsNullOrEmpty(linkToken)
            ? $"/api/auth/callback/{provider}"
            : $"/api/auth/callback/{provider}?link_token={Uri.EscapeDataString(linkToken)}";
        var properties = new AuthenticationProperties { RedirectUri = redirectUri };

        return provider switch
        {
            "google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
            "microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
            _ => Redirect($"{appSettings.ClientUrl}/auth-error")
        };
    }

    /// <summary>
    /// OAuth callback handler — runs after the provider redirects back. Reads the
    /// external principal (carried in the External cookie scheme), resolves/creates
    /// the account, issues a refresh-token cookie, and redirects to the client.
    /// The JWT is never placed in the URL (it would leak via history/logs).
    /// </summary>
    [HttpGet("callback/{provider}")]
    [Authorize(AuthenticationSchemes = ServiceCollectionExtensions.ExternalScheme)]
    public async Task<IActionResult> Callback(string provider, [FromQuery(Name = "link_token")] string? linkToken = null)
    {
        try
        {
            provider = provider.ToLowerInvariant();
            if (!SupportedProviders.Contains(provider))
            {
                logger.LogWarning("OAuth callback for unsupported provider: {Provider}", provider);
                return Redirect($"{appSettings.ClientUrl}/auth-error");
            }

            var (_, providerUserId, email) = claimsExtractor.ExtractClaims(User);

            if (string.IsNullOrEmpty(providerUserId) || string.IsNullOrEmpty(email))
            {
                logger.LogWarning("OAuth callback: missing claims");
                return Redirect($"{appSettings.ClientUrl}/auth-error");
            }

            // LINK MODE: attach this identity to the initiating account, don't sign in.
            if (!string.IsNullOrEmpty(linkToken))
            {
                var linkUserId = linkTokenService.Redeem(linkToken);
                await HttpContext.SignOutAsync(ServiceCollectionExtensions.ExternalScheme);

                if (linkUserId is null)
                    return Redirect($"{appSettings.ClientUrl}/settings?link_error=expired");

                var linkResult = await userService.LinkLoginAsync(linkUserId.Value, provider, providerUserId);
                logger.LogInformation("Link {Provider} to user {UserId}: {Result}", provider, linkUserId, linkResult);
                return linkResult == LinkLoginResult.OwnedByAnotherAccount
                    ? Redirect($"{appSettings.ClientUrl}/settings?link_error=in_use")
                    : Redirect($"{appSettings.ClientUrl}/settings?linked={provider}");
            }

            var user = await userService.GetOrCreateUserAsync(email, providerUserId, provider,
                claimsExtractor.ExtractDisplayName(User), claimsExtractor.IsEmailVerified(User));

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var issued = await refreshTokenService.IssueRefreshTokenAsync(user.Id, ipAddress, provider);

            cookieService.SetRefreshTokenCookie(Response, issued.RawToken, Request);

            // Sign the external carrier cookie out — its job is done.
            await HttpContext.SignOutAsync(ServiceCollectionExtensions.ExternalScheme);

            logger.LogInformation("OAuth callback successful for user: {Email} via {Provider}", email, provider);

            return Redirect($"{appSettings.ClientUrl}/auth-callback");
        }
        catch (UnverifiedEmailConflictException)
        {
            return Redirect($"{appSettings.ClientUrl}/login?error=email_unverified");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth callback failed");
            return Redirect($"{appSettings.ClientUrl}/auth-error");
        }
    }

    /// <summary>
    /// Exchanges the refresh-token cookie for a fresh access token, rotating the
    /// refresh token so a stolen cookie can only be replayed once.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            var rawToken = cookieService.GetRefreshTokenFromCookies(Request);
            if (string.IsNullOrEmpty(rawToken))
                return Unauthorized(errorFactory.CreateError("no_refresh_token", "Refresh token not found"));

            var validToken = await refreshTokenService.ValidateRefreshTokenAsync(rawToken);
            if (validToken == null)
                return Unauthorized(errorFactory.CreateError("invalid_refresh_token", "Refresh token is invalid or expired"));

            var user = await userService.GetUserByIdAsync(validToken.UserId);
            if (user == null)
                return Unauthorized(errorFactory.CreateError("user_not_found", "User not found"));

            // Rotate: revoke the used token, issue a new one.
            await refreshTokenService.RevokeRefreshTokenAsync(validToken.Id);

            var tenantName = await ResolveTenantNameAsync(user.Id);
            var newJwt = jwtTokenService.IssueAccessToken(
                user.Id, user.Email, validToken.Provider, user.DisplayName, tenantName);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var issued = await refreshTokenService.IssueRefreshTokenAsync(user.Id, ipAddress, validToken.Provider);

            cookieService.SetRefreshTokenCookie(Response, issued.RawToken, Request);

            logger.LogInformation("Token refreshed for user: {UserId}", validToken.UserId);

            return Ok(new TokenResponse
            {
                AccessToken = newJwt,
                ExpiresIn = jwtSettings.ExpiryMinutes * 60,
                UserId = user.Id,
                Email = user.Email
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token refresh failed");
            return StatusCode(500, errorFactory.CreateError("refresh_failed", "Failed to refresh token"));
        }
    }

    /// <summary>
    /// Revokes all refresh tokens for the session and deletes the cookie.
    /// Identifies the user by the refresh cookie (not [Authorize]) so it works
    /// even with an expired access token. Idempotent.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var rawToken = cookieService.GetRefreshTokenFromCookies(Request);
            if (!string.IsNullOrEmpty(rawToken))
            {
                var token = await refreshTokenService.ValidateRefreshTokenAsync(rawToken);
                if (token != null)
                {
                    await refreshTokenService.RevokeAllUserTokensAsync(token.UserId);
                    logger.LogInformation("User logout: {UserId}", token.UserId);
                }
            }

            cookieService.DeleteRefreshTokenCookie(Response);
            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Logout failed");
            return StatusCode(500, errorFactory.CreateError("logout_failed", "Failed to logout"));
        }
    }

    /// <summary>
    /// Returns the signed-in user's display info for the client top bar.
    /// Authenticated via the JWT Bearer access token.
    /// </summary>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Me()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var user = await userService.GetUserByIdAsync(userId);
        if (user == null)
            return Unauthorized();

        var tenantName = await ResolveTenantNameAsync(user.Id);
        return Ok(new UserProfileResponse
        {
            UserName = user.DisplayName ?? user.Email,
            TenantName = tenantName ?? string.Empty
        });
    }

    // ── Account linking ──────────────────────────────────────────────────────

    /// <summary>
    /// Lists the OAuth providers linked to the signed-in account (for the settings page).
    /// </summary>
    [HttpGet("logins")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logins()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var logins = await userLoginRepository.GetForUserAsync(userId);
        return Ok(logins.Select(l => new { provider = l.Provider, linkedAt = l.CreatedAt }));
    }

    /// <summary>
    /// Issues a single-use link token for the current user and returns the provider
    /// sign-in URL carrying it. The client full-page-navigates there to link the
    /// provider to this account (no new user is created).
    /// </summary>
    [HttpPost("link/{provider}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult StartLink(string provider)
    {
        provider = provider.ToLowerInvariant();
        if (!SupportedProviders.Contains(provider))
            return BadRequest(errorFactory.CreateError("unsupported_provider", "Unknown provider."));
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var token = linkTokenService.Issue(userId);
        var url = $"{Request.Scheme}://{Request.Host}/api/auth/login/{provider}" +
                  $"?link_token={Uri.EscapeDataString(token)}";
        return Ok(new { url });
    }

    /// <summary>
    /// Unlinks an OAuth provider from the account. Sign-in by email (magic link / OTP)
    /// always remains available, so removing a provider can't lock the user out.
    /// </summary>
    [HttpDelete("logins/{provider}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Unlink(string provider)
    {
        provider = provider.ToLowerInvariant();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var login = await userLoginRepository.GetByProviderForUserAsync(userId, provider);
        if (login is null) return NotFound();

        await userLoginRepository.DeleteAsync(login);
        logger.LogInformation("Unlinked {Provider} from user {UserId}", provider, userId);
        return Ok();
    }

    // ── Passwordless: magic link (web) ───────────────────────────────────────

    /// <summary>
    /// Emails a single-use sign-in link. Always returns 200 — it never reveals
    /// whether an account exists for the address.
    /// </summary>
    [HttpPost("magic-link/send")]
    public async Task<IActionResult> SendMagicLink([FromBody] EmailRequest req)
    {
        if (!IsLikelyEmail(req.Email))
            return BadRequest(errorFactory.CreateError("invalid_email", "A valid email address is required."));

        var email = req.Email.Trim();
        var token = await passwordless.IssueMagicLinkTokenAsync(email);
        var link = $"{Request.Scheme}://{Request.Host}/api/auth/magic-link/verify" +
                   $"?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

        await emailSender.SendAsync(email, "Your sign-in link",
            $"""
             <p>Click the link below to sign in. It expires in {passwordlessSettings.MagicLinkLifespanMinutes} minutes.</p>
             <p><a href="{link}">Sign in</a></p>
             <p>If you didn't request this, you can safely ignore this email.</p>
             """);

        return Ok();
    }

    /// <summary>
    /// Validates a magic-link token, establishes the session (refresh cookie), and
    /// bounces to the client callback. The JWT is never put in the URL.
    /// </summary>
    [HttpGet("magic-link/verify")]
    public async Task<IActionResult> VerifyMagicLink([FromQuery] string token, [FromQuery] string email)
    {
        var user = await passwordless.RedeemMagicLinkAsync(email, token);
        if (user is null)
            return Redirect($"{appSettings.ClientUrl}/login?error=invalid_link");

        await IssueRefreshCookieAsync(user.Id, LoginTokenPurpose.MagicLink);
        return Redirect($"{appSettings.ClientUrl}/auth-callback");
    }

    // ── Passwordless: OTP (web + future mobile) ──────────────────────────────

    /// <summary>Emails a single-use numeric code. Always returns 200 (no enumeration).</summary>
    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] EmailRequest req)
    {
        if (!IsLikelyEmail(req.Email))
            return BadRequest(errorFactory.CreateError("invalid_email", "A valid email address is required."));

        var email = req.Email.Trim();
        var code = await passwordless.IssueOtpAsync(email);

        await emailSender.SendAsync(email, "Your verification code",
            $"""
             <p>Your verification code is:</p>
             <p style="font-size:24px;font-weight:bold;letter-spacing:3px">{code}</p>
             <p>It expires in {passwordlessSettings.OtpLifespanMinutes} minutes.</p>
             """);

        return Ok();
    }

    /// <summary>
    /// Verifies an OTP code. On success sets the refresh cookie (web) and also
    /// returns the access token (mobile/API clients that hold the JWT directly).
    /// </summary>
    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest req)
    {
        var result = await passwordless.RedeemOtpAsync(req.Email, req.Code);
        if (result.Status != OtpStatus.Success || result.User is null)
        {
            var code = result.Status switch
            {
                OtpStatus.TooManyAttempts => "too_many_attempts",
                OtpStatus.Expired => "code_expired",
                _ => "invalid_code"
            };
            return Unauthorized(errorFactory.CreateError(code, "The code is incorrect or has expired."));
        }

        await IssueRefreshCookieAsync(result.User.Id, LoginTokenPurpose.Otp);

        var tenantName = await ResolveTenantNameAsync(result.User.Id);
        var jwt = jwtTokenService.IssueAccessToken(
            result.User.Id, result.User.Email, LoginTokenPurpose.Otp, result.User.DisplayName, tenantName);

        return Ok(new TokenResponse
        {
            AccessToken = jwt,
            ExpiresIn = jwtSettings.ExpiryMinutes * 60,
            UserId = result.User.Id,
            Email = result.User.Email
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the user's tenant name via their membership (the source of truth),
    /// for the JWT tenant_name claim and the /me profile. Null when the user has no
    /// membership.
    /// </summary>
    private async Task<string?> ResolveTenantNameAsync(Guid userId)
    {
        var membership = await tenantRepository.GetMembershipAsync(userId);
        if (membership is null) return null;
        var tenant = await tenantRepository.GetByIdAsync(membership.TenantId);
        return tenant?.Name;
    }

    private async Task IssueRefreshCookieAsync(Guid userId, string provider)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var issued = await refreshTokenService.IssueRefreshTokenAsync(userId, ip, provider);
        cookieService.SetRefreshTokenCookie(Response, issued.RawToken, Request);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool IsLikelyEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Contains('.');
}

public record EmailRequest(string Email);

public record OtpVerifyRequest(string Email, string Code);
