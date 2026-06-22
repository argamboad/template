using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Api.Configuration;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure;
using Template.Infrastructure.Email;

namespace Template.Api.Controllers;

/// <summary>
/// Authentication controller for the OAuth → JWT + refresh-token flow.
/// Focused solely on HTTP request/response handling; delegates to services.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    IUserService userService,
    ISessionService sessionService,
    IRefreshTokenService refreshTokenService,
    ICookieService cookieService,
    IClaimsExtractor claimsExtractor,
    IPasswordlessService passwordless,
    ILinkTokenService linkTokenService,
    INativeAuthCodeService nativeAuthCodeService,
    IUserLoginRepository userLoginRepository,
    IEmailSender emailSender,
    IErrorResponseFactory errorFactory,
    IApplicationSettings appSettings,
    IPasswordlessSettings passwordlessSettings,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly string[] SupportedLocales = ["en", "es", "fr", "de", "pt"];

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

        var scheme = AuthProviders.SchemeFor(provider);
        return scheme is null
            ? Redirect($"{appSettings.ClientUrl}/auth-error")
            : Challenge(properties, scheme);
    }

    /// <summary>
    /// OAuth callback handler — runs after the provider redirects back. Reads the
    /// external principal (carried in the External cookie scheme), resolves/creates
    /// the account, issues a refresh-token cookie, and redirects to the client.
    /// The JWT is never placed in the URL (it would leak via history/logs).
    /// </summary>
    [HttpGet("callback/{provider}")]
    [Authorize(AuthenticationSchemes = ServiceCollectionExtensions.ExternalScheme)]
    public async Task<IActionResult> Callback(string provider, CancellationToken cancellationToken, [FromQuery(Name = "link_token")] string? linkToken = null)
    {
        try
        {
            provider = provider.ToLowerInvariant();
            if (!AuthProviders.IsSupported(provider))
            {
                logger.LogWarning("OAuth callback for unsupported provider: {Provider}", provider);
                return Redirect($"{appSettings.ClientUrl}/auth-error");
            }

            var (providerUserId, email) = claimsExtractor.ExtractClaims(User);

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

                var linkResult = await userService.LinkLoginAsync(linkUserId.Value, provider, providerUserId, cancellationToken);
                logger.LogInformation("Link {Provider} to user {UserId}: {Result}", provider, linkUserId, linkResult);
                return linkResult == LinkLoginResult.OwnedByAnotherAccount
                    ? Redirect($"{appSettings.ClientUrl}/settings?link_error=in_use")
                    : Redirect($"{appSettings.ClientUrl}/settings?linked={provider}");
            }

            var user = await userService.GetOrCreateUserAsync(email, providerUserId, provider,
                claimsExtractor.ExtractDisplayName(User), claimsExtractor.IsEmailVerified(User), cancellationToken);

            var issued = await refreshTokenService.IssueRefreshTokenAsync(user.Id, ClientIp, provider, cancellationToken);
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
    /// Exchanges a refresh token for a fresh access token, rotating the refresh token: the used
    /// token is revoked and a new one issued. If an already-rotated (revoked) token is later
    /// replayed, that's treated as token theft — every session for the user is revoked and the
    /// event is audit-logged (the client sees the same generic error as any invalid token, so the
    /// reuse signal isn't leaked). Transport depends on the client: the browser sends/receives the
    /// token via the HttpOnly cookie; a native client (header <c>X-Native-Client: true</c>) sends it
    /// in the body and gets the rotated token back in the body — it never had a cookie to begin with.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? req = null)
    {
        try
        {
            var native = IsNativeClient;
            var rawToken = native ? req?.RefreshToken : cookieService.GetRefreshTokenFromCookies(Request);
            if (string.IsNullOrEmpty(rawToken))
                return Unauthorized(errorFactory.CreateError("no_refresh_token", "Refresh token not found"));

            var inspection = await refreshTokenService.InspectRefreshTokenAsync(rawToken, cancellationToken);
            if (inspection.Status == RefreshTokenStatus.Reuse)
            {
                // Replay of a rotated-out token ⇒ assume theft: revoke every session for the user.
                // Client still gets the generic error below, so the reuse signal isn't leaked.
                await refreshTokenService.RevokeAllUserTokensAsync(inspection.Token!.UserId, cancellationToken);
                logger.LogWarning("Refresh-token reuse detected for user {UserId}; revoked all sessions", inspection.Token.UserId);
            }
            if (inspection.Status != RefreshTokenStatus.Valid)
                return Unauthorized(errorFactory.CreateError("invalid_refresh_token", "Refresh token is invalid or expired"));

            var validToken = inspection.Token!;
            var user = await userService.GetUserByIdAsync(validToken.UserId, cancellationToken);
            if (user == null)
                return Unauthorized(errorFactory.CreateError("user_not_found", "User not found"));

            // Rotate: revoke the used token, then issue a fresh session.
            await refreshTokenService.RevokeRefreshTokenAsync(validToken.Id, cancellationToken);

            var session = await sessionService.IssueAsync(user, validToken.Provider, ClientIp, native, cancellationToken);

            // Web: rotate the cookie. Native: the rotated token is already on the body.
            if (!native)
                cookieService.SetRefreshTokenCookie(Response, session.RefreshToken, Request);

            logger.LogInformation("Token refreshed for user: {UserId}", validToken.UserId);

            return Ok(session.Response);
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
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? req = null)
    {
        try
        {
            var native = IsNativeClient;
            var rawToken = native ? req?.RefreshToken : cookieService.GetRefreshTokenFromCookies(Request);
            if (!string.IsNullOrEmpty(rawToken))
            {
                var token = await refreshTokenService.ValidateRefreshTokenAsync(rawToken, cancellationToken);
                if (token != null)
                {
                    await refreshTokenService.RevokeAllUserTokensAsync(token.UserId, cancellationToken);
                    logger.LogInformation("User logout: {UserId}", token.UserId);
                }
            }

            // Native clients have no cookie to clear; they drop the token from secure storage.
            if (!native)
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
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        var user = await userService.GetUserByIdAsync(userId, cancellationToken);
        if (user == null)
            return Unauthorized(errorFactory.CreateError("user_not_found", "User not found"));

        var (_, tenantName) = await sessionService.ResolveTenantAsync(user.Id, cancellationToken);
        return Ok(new UserProfileResponse
        {
            UserName = user.DisplayName ?? user.Email,
            TenantName = tenantName ?? string.Empty
        });
    }

    /// <summary>
    /// Saves the signed-in user's preferred UI language so it follows them across
    /// devices. The new value lands in the JWT on the next refresh.
    /// </summary>
    [HttpPut("locale")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> SetLocale([FromBody] LocaleRequest req, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var locale = req.Locale?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(locale) || !SupportedLocales.Contains(locale))
            return BadRequest(errorFactory.CreateError("unsupported_locale", "Unsupported locale."));

        await userService.UpdateLocaleAsync(userId, locale, cancellationToken);
        return Ok();
    }

    // ── Account linking ──────────────────────────────────────────────────────

    /// <summary>
    /// Lists the OAuth providers linked to the signed-in account (for the settings page).
    /// </summary>
    [HttpGet("logins")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logins(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var logins = await userLoginRepository.GetForUserAsync(userId, cancellationToken);
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
        if (!AuthProviders.IsSupported(provider))
            return BadRequest(errorFactory.CreateError("unsupported_provider", "Unknown provider."));
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var token = linkTokenService.Issue(userId);
        var url = $"{Request.Scheme}://{Request.Host}/api/auth/login/{provider}" +
                  $"?link_token={Uri.EscapeDataString(token)}";
        // url: web full-page navigates to it. token: native carries it through the
        // loopback OAuth flow (native/login?...&link_token=) instead.
        return Ok(new { url, token });
    }

    /// <summary>
    /// Unlinks an OAuth provider from the account. Sign-in by email (magic link / OTP)
    /// always remains available, so removing a provider can't lock the user out.
    /// </summary>
    [HttpDelete("logins/{provider}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Unlink(string provider, CancellationToken cancellationToken)
    {
        provider = provider.ToLowerInvariant();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var login = await userLoginRepository.GetByProviderForUserAsync(userId, provider, cancellationToken);
        if (login is null) return NotFound();

        await userLoginRepository.DeleteAsync(login, cancellationToken);
        logger.LogInformation("Unlinked {Provider} from user {UserId}", provider, userId);
        return Ok();
    }

    // ── Passwordless: magic link (web) ───────────────────────────────────────

    /// <summary>
    /// Emails a single-use sign-in link. Always returns 200 — it never reveals
    /// whether an account exists for the address.
    /// </summary>
    [HttpPost("magic-link/send")]
    public async Task<IActionResult> SendMagicLink([FromBody] EmailRequest req, CancellationToken cancellationToken)
    {
        if (!IsLikelyEmail(req.Email))
            return BadRequest(errorFactory.CreateError("invalid_email", "A valid email address is required."));

        var email = req.Email.Trim();
        var token = await passwordless.IssueMagicLinkTokenAsync(email, cancellationToken);
        var link = $"{Request.Scheme}://{Request.Host}/api/auth/magic-link/verify" +
                   $"?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

        var emailBody = BrandedEmail.MagicLink(link, passwordlessSettings.MagicLinkLifespanMinutes,
            BrandedEmail.ResolveCulture(req.Culture));
        await emailSender.SendAsync(email, emailBody.Subject, emailBody.Html, emailBody.InlineImages);

        return Ok();
    }

    /// <summary>
    /// Validates a magic-link token, establishes the session (refresh cookie), and
    /// bounces to the client callback. The JWT is never put in the URL.
    /// </summary>
    [HttpGet("magic-link/verify")]
    public async Task<IActionResult> VerifyMagicLink([FromQuery] string token, [FromQuery] string email, CancellationToken cancellationToken)
    {
        var user = await passwordless.RedeemMagicLinkAsync(email, token, cancellationToken);
        if (user is null)
            return Redirect($"{appSettings.ClientUrl}/login?error=invalid_link");

        await IssueRefreshCookieAsync(user.Id, LoginTokenPurpose.MagicLink, cancellationToken);
        return Redirect($"{appSettings.ClientUrl}/auth-callback");
    }

    // ── Passwordless: OTP (web + future mobile) ──────────────────────────────

    /// <summary>Emails a single-use numeric code. Always returns 200 (no enumeration).</summary>
    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] EmailRequest req, CancellationToken cancellationToken)
    {
        if (!IsLikelyEmail(req.Email))
            return BadRequest(errorFactory.CreateError("invalid_email", "A valid email address is required."));

        var email = req.Email.Trim();
        var code = await passwordless.IssueOtpAsync(email, cancellationToken);

        var emailBody = BrandedEmail.Otp(code, passwordlessSettings.OtpLifespanMinutes,
            BrandedEmail.ResolveCulture(req.Culture));
        await emailSender.SendAsync(email, emailBody.Subject, emailBody.Html, emailBody.InlineImages);

        return Ok();
    }

    /// <summary>
    /// Verifies an OTP code and establishes the session. The browser gets a refresh
    /// cookie; a native client (header <c>X-Native-Client: true</c>) gets the refresh
    /// token in the body to persist in its OS secure store. Both get the access token.
    /// </summary>
    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest req, CancellationToken cancellationToken)
    {
        var result = await passwordless.RedeemOtpAsync(req.Email, req.Code, cancellationToken);
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

        var native = IsNativeClient;
        var session = await sessionService.IssueAsync(result.User, LoginTokenPurpose.Otp, ClientIp, native, cancellationToken);
        if (!native)
            cookieService.SetRefreshTokenCookie(Response, session.RefreshToken, Request);

        return Ok(session.Response);
    }

    // ── Native (desktop/mobile) OAuth: loopback / custom-scheme code flow ─────

    /// <summary>
    /// Starts OAuth for a native client. The app opens this URL in the system browser
    /// (passing a loopback <paramref name="redirect"/> it's listening on); the provider
    /// round-trip lands on the native callback, which hands back a one-time code.
    /// </summary>
    [HttpGet("native/login/{provider}")]
    public IActionResult NativeLogin(string provider, [FromQuery] string redirect,
        [FromQuery(Name = "link_token")] string? linkToken = null)
    {
        provider = provider.ToLowerInvariant();
        if (!AuthProviders.IsSupported(provider) || !IsAllowedNativeRedirect(redirect))
            return BadRequest(errorFactory.CreateError("invalid_request", "Unsupported provider or redirect target."));

        var callback = $"/api/auth/native/callback/{provider}?redirect={Uri.EscapeDataString(redirect)}";
        if (!string.IsNullOrEmpty(linkToken))
            callback += $"&link_token={Uri.EscapeDataString(linkToken)}";
        var properties = new AuthenticationProperties { RedirectUri = callback };

        var scheme = AuthProviders.SchemeFor(provider);
        return scheme is null
            ? BadRequest(errorFactory.CreateError("invalid_request", "Unsupported provider."))
            : Challenge(properties, scheme);
    }

    /// <summary>
    /// Native OAuth callback. Resolves/creates the account, mints a single-use code,
    /// and redirects to the app's loopback/scheme URL carrying ONLY that code — tokens
    /// never travel in the URL. The app exchanges the code at /native/exchange.
    /// </summary>
    [HttpGet("native/callback/{provider}")]
    [Authorize(AuthenticationSchemes = ServiceCollectionExtensions.ExternalScheme)]
    public async Task<IActionResult> NativeCallback(string provider, [FromQuery] string redirect,
        CancellationToken cancellationToken, [FromQuery(Name = "link_token")] string? linkToken = null)
    {
        provider = provider.ToLowerInvariant();
        try
        {
            if (!AuthProviders.IsSupported(provider) || !IsAllowedNativeRedirect(redirect))
                return BadRequest(errorFactory.CreateError("invalid_request", "Unsupported provider or redirect target."));

            var (providerUserId, email) = claimsExtractor.ExtractClaims(User);
            await HttpContext.SignOutAsync(ServiceCollectionExtensions.ExternalScheme);

            if (string.IsNullOrEmpty(providerUserId) || string.IsNullOrEmpty(email))
                return Redirect(AppendQuery(redirect, "error", "auth_failed"));

            // LINK MODE: attach this identity to the initiating account, don't sign in.
            if (!string.IsNullOrEmpty(linkToken))
            {
                var linkUserId = linkTokenService.Redeem(linkToken);
                if (linkUserId is null)
                    return Redirect(AppendQuery(redirect, "error", "expired"));

                var linkResult = await userService.LinkLoginAsync(linkUserId.Value, provider, providerUserId, cancellationToken);
                logger.LogInformation("Native link {Provider} to user {UserId}: {Result}", provider, linkUserId, linkResult);
                return linkResult == LinkLoginResult.OwnedByAnotherAccount
                    ? Redirect(AppendQuery(redirect, "error", "in_use"))
                    : Redirect(AppendQuery(redirect, "linked", provider));
            }

            var user = await userService.GetOrCreateUserAsync(email, providerUserId, provider,
                claimsExtractor.ExtractDisplayName(User), claimsExtractor.IsEmailVerified(User), cancellationToken);

            var code = nativeAuthCodeService.Issue(user.Id, provider);
            logger.LogInformation("Native OAuth callback successful for {Email} via {Provider}", email, provider);
            return Redirect(AppendQuery(redirect, "code", code));
        }
        catch (UnverifiedEmailConflictException)
        {
            return Redirect(AppendQuery(redirect, "error", "email_unverified"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native OAuth callback failed");
            return Redirect(AppendQuery(redirect, "error", "auth_failed"));
        }
    }

    /// <summary>
    /// Exchanges a single-use native-auth code for an access token + refresh token
    /// (both in the body). The code is consumed on first use.
    /// </summary>
    [HttpPost("native/exchange")]
    public async Task<IActionResult> NativeExchange([FromBody] NativeExchangeRequest req, CancellationToken cancellationToken)
    {
        var grant = nativeAuthCodeService.Redeem(req.Code);
        if (grant is null)
            return Unauthorized(errorFactory.CreateError("invalid_code", "The code is invalid or has expired."));

        var user = await userService.GetUserByIdAsync(grant.Value.UserId, cancellationToken);
        if (user is null)
            return Unauthorized(errorFactory.CreateError("user_not_found", "User not found"));

        // Native exchange always returns the refresh token in the body.
        var session = await sessionService.IssueAsync(user, grant.Value.Provider, ClientIp, native: true, cancellationToken);
        return Ok(session.Response);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Caller IP for refresh-token auditing; "unknown" when unavailable.</summary>
    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Issues a refresh token and sets it as the browser cookie — for web-only paths
    /// (OAuth callback, magic link) where the client then calls /refresh for its JWT.
    /// </summary>
    private async Task IssueRefreshCookieAsync(Guid userId, string provider, CancellationToken cancellationToken = default)
    {
        var issued = await refreshTokenService.IssueRefreshTokenAsync(userId, ClientIp, provider, cancellationToken);
        cookieService.SetRefreshTokenCookie(Response, issued.RawToken, Request);
    }

    /// <summary>True when the request comes from a native (desktop/mobile) client.</summary>
    private bool IsNativeClient => Request.Headers[AuthHeaders.NativeClient] == AuthHeaders.NativeClientValue;

    /// <summary>
    /// Whether the native client's redirect target is permitted. Loopback HTTP
    /// (127.0.0.1 / localhost, any port) is always allowed — the desktop pattern
    /// (RFC 8252 §7.3). A configured custom scheme (mobile) is allowed too. Anything
    /// else is rejected to prevent the callback being used as an open redirect.
    /// </summary>
    private bool IsAllowedNativeRedirect(string? redirect) =>
        NativeRedirectPolicy.IsAllowed(redirect, appSettings.NativeCallbackScheme);

    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{key}={Uri.EscapeDataString(value)}";
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool IsLikelyEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _);
}

public record EmailRequest(string Email, string? Culture = null);

public record OtpVerifyRequest(string Email, string Code);

public record RefreshRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken);

public record NativeExchangeRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code);

public record LocaleRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("locale")] string? Locale);
