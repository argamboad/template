using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Template.Shared.Ui.Auth;

/// <summary>Outcome of a native primary-auth attempt: signed in, failed, or owes an MFA step-up.</summary>
public enum SignInStatus { Success, Failed, MfaRequired }

/// <summary>A native sign-in result; carries the MFA challenge when <see cref="SignInStatus.MfaRequired"/>.</summary>
public sealed record SignInResult(SignInStatus Status, string? Challenge = null)
{
    public static readonly SignInResult Failed = new(SignInStatus.Failed);
    public static readonly SignInResult Success = new(SignInStatus.Success);
    public static SignInResult Mfa(string challenge) => new(SignInStatus.MfaRequired, challenge);
}

/// <summary>
/// Client-side authentication with refresh-token support.
/// The access token is held in memory only — never persisted.
/// Sessions survive reloads/restarts via the refresh token: on startup we silently
/// exchange it for a fresh access token. Where that refresh token lives is the only
/// per-host difference, abstracted behind <see cref="ISessionStore"/> — an HttpOnly
/// cookie on the web, the OS secure store on native (MAUI).
/// </summary>
public class AuthService(
    HttpClient httpClient,
    ILogger<AuthService> logger,
    ISessionStore sessionStore,
    IOAuthInitiator? oauth = null)
{
    private string? _accessToken;
    private Task<bool>? _refreshInFlight;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && !IsTokenExpired(_accessToken);

    /// <summary>
    /// True on native hosts (MAUI). The Login page uses it to swap the web's full-page
    /// OAuth navigation for the native browser flow and to drop the web-only magic link.
    /// </summary>
    public bool IsNative => sessionStore.UsesBodyTransport;

    /// <summary>The current JWT access token, or null when not signed in.</summary>
    public string? AccessToken => _accessToken;

    /// <summary>The signed-in user's id (from the JWT NameIdentifier claim), or null.</summary>
    public Guid? UserId
    {
        get
        {
            if (string.IsNullOrEmpty(_accessToken) || IsTokenExpired(_accessToken))
                return null;
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_accessToken);
                var sub = jwt.Claims.FirstOrDefault(c =>
                    c.Type is "nameid" or ClaimTypes.NameIdentifier or "sub")?.Value;
                return Guid.TryParse(sub, out var id) ? id : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Attempts a silent refresh using the HttpOnly refresh cookie. Returns true
    /// when a fresh access token was obtained. Called on startup and on expiry.
    /// Concurrent callers share one in-flight call: the refresh endpoint ROTATES the
    /// token, so two overlapping requests would make the second fail with a revoked
    /// token. MainLayout is the single refresh entry point now, so this is mostly
    /// defense-in-depth. Blazor WASM is single-threaded, so sharing the Task suffices.
    /// </summary>
    public Task<bool> TryRefreshAsync() => _refreshInFlight ??= RunRefreshAsync();

    private async Task<bool> RunRefreshAsync()
    {
        try
        {
            HttpResponseMessage response;
            if (sessionStore.UsesBodyTransport)
            {
                // Native: the refresh token lives in the OS secure store; send it in the
                // body. No stored token means simply "not signed in" — skip the call.
                var stored = await sessionStore.GetRefreshTokenAsync();
                if (string.IsNullOrEmpty(stored))
                {
                    _accessToken = null;
                    return false;
                }
                response = await httpClient.PostAsJsonAsync("/api/auth/refresh",
                    new { refresh_token = stored });
            }
            else
            {
                // Web: the CookieHandler attaches the HttpOnly refresh cookie; no body.
                response = await httpClient.PostAsync("/api/auth/refresh", null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Token refresh failed: {StatusCode}", response.StatusCode);
                await ClearSessionAsync();
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (!string.IsNullOrEmpty(payload?.AccessToken))
            {
                await AcceptTokensAsync(payload);
                return true;
            }

            logger.LogWarning("Refresh response missing access_token");
            await ClearSessionAsync();
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh access token");
            await ClearSessionAsync();
            return false;
        }
        finally
        {
            // Allow a fresh refresh next time; only *concurrent* calls are coalesced.
            _refreshInFlight = null;
        }
    }

    /// <summary>
    /// Completes native OAuth: runs the platform browser flow, exchanges the returned
    /// one-time code for tokens, and stores them. Returns true on success. Web hosts
    /// sign in by full-page navigation and never call this.
    /// </summary>
    public async Task<SignInResult> SignInWithOAuthAsync(string provider)
    {
        if (oauth is null)
        {
            logger.LogError("SignInWithOAuthAsync called with no IOAuthInitiator registered");
            return SignInResult.Failed;
        }
        try
        {
            var result = await oauth.RunBrowserFlowAsync(provider);
            var code = result is not null && result.TryGetValue("code", out var c) ? c : null;
            if (string.IsNullOrEmpty(code))
                return SignInResult.Failed;

            // The exchange returns tokens — or, if the user has MFA on, an {mfa_required, challenge}.
            var response = await httpClient.PostAsJsonAsync("/api/auth/native/exchange", new { code });
            return await CompleteFromResponseAsync(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native OAuth sign-in failed for {Provider}", provider);
            return SignInResult.Failed;
        }
    }

    /// <summary>
    /// Links an OAuth provider to the current account on native hosts, carrying the
    /// caller-issued <paramref name="linkToken"/> through the system-browser flow.
    /// Returns null on success, or an error key ("in_use", "expired", "cancelled",
    /// "link_failed") for the UI.
    /// </summary>
    public async Task<string?> LinkProviderAsync(string provider, string linkToken)
    {
        if (oauth is null)
            return "unsupported";
        try
        {
            var result = await oauth.RunBrowserFlowAsync(provider, linkToken);
            if (result is null)
                return "cancelled";
            if (result.TryGetValue("error", out var error))
                return string.IsNullOrEmpty(error) ? "link_failed" : error;
            return result.ContainsKey("linked") ? null : "link_failed";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native provider link failed for {Provider}", provider);
            return "link_failed";
        }
    }

    /// <summary>
    /// Verifies an OTP code and establishes the session from the tokens in the response
    /// body. Used by native hosts (the web Login page keeps its cookie + callback flow).
    /// </summary>
    public async Task<SignInResult> VerifyOtpAsync(string email, string code)
    {
        try
        {
            // Returns tokens — or, if the user has MFA on, an {mfa_required, challenge} to step up.
            var response = await httpClient.PostAsJsonAsync("/api/auth/otp/verify",
                new { email, code });
            return await CompleteFromResponseAsync(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OTP verification failed");
            return SignInResult.Failed;
        }
    }

    /// <summary>
    /// Completes a native MFA step-up: posts the challenge from a prior login + a TOTP/recovery code and
    /// stores the tokens the API returns in the body. Returns true on success. Web hosts complete the
    /// step-up via the cookie flow (Login page) and don't call this.
    /// </summary>
    public async Task<bool> VerifyMfaAsync(string challenge, string code)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/auth/mfa/verify", new { challenge, code });
            var result = await CompleteFromResponseAsync(response);
            return result.Status == SignInStatus.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MFA verification failed");
            return false;
        }
    }

    /// <summary>
    /// Reads a native auth response: an <c>{mfa_required, challenge}</c> body means step up; otherwise the
    /// tokens are accepted and stored. Shared by the OTP, OAuth-exchange and MFA-verify paths.
    /// </summary>
    private async Task<SignInResult> CompleteFromResponseAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Native auth call failed: {StatusCode}", response.StatusCode);
            return SignInResult.Failed;
        }

        var payload = await response.Content.ReadFromJsonAsync<NativeAuthResponse>();
        if (payload is null)
            return SignInResult.Failed;

        if (payload.MfaRequired && !string.IsNullOrEmpty(payload.Challenge))
            return SignInResult.Mfa(payload.Challenge);

        if (string.IsNullOrEmpty(payload.AccessToken))
            return SignInResult.Failed;

        await AcceptTokensAsync(new TokenResponse { AccessToken = payload.AccessToken, RefreshToken = payload.RefreshToken });
        return SignInResult.Success;
    }

    // ── Platform-staff admin surface (ADR-014) ──────────────────────────────

    // Cache the staff probe for the session so nav rendering doesn't re-hit the API.
    // Reset whenever the identity changes (impersonate/stop/logout).
    private bool? _isStaff;

    /// <summary>
    /// Whether the signed-in user is platform staff — drives the admin nav link + page gate.
    /// Cheap probe of <c>GET /api/admin/me</c> (200 with <c>is_staff</c> for any authenticated user);
    /// cached per identity. False when signed out, on any error, or while impersonating.
    /// </summary>
    public async Task<bool> IsStaffAsync()
    {
        if (_isStaff is { } cached) return cached;
        if (!IsAuthenticated || IsImpersonating) return (_isStaff = false).Value;
        try
        {
            // This service's HttpClient deliberately has NO Bearer handler (it would be a DI cycle —
            // see Program.cs), so attach the in-memory token explicitly for this authenticated probe.
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return false; // don't cache transient failures
            var res = await response.Content.ReadFromJsonAsync<StaffStatus>();
            return (_isStaff = res?.IsStaff ?? false).Value;
        }
        catch
        {
            return false; // don't cache transient failures
        }
    }

    /// <summary>
    /// The OAuth providers the server has actually configured (lowercase, e.g. "google"). Anonymous
    /// probe of <c>GET /api/auth/providers</c> — the login + settings pages render only these, so an
    /// unconfigured provider shows no dead button (challenging it 500s). Empty on any error (fail closed
    /// to no OAuth rather than a broken button).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEnabledProvidersAsync()
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ProvidersResponse>("/api/auth/providers");
            return res?.Providers ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>True when the current access token is an admin "sign in as" token.</summary>
    public bool IsImpersonating => Claim(AppClaims.ImpersonatedBy) is not null;

    /// <summary>
    /// Enters an impersonated session using a short-lived admin token (no refresh token — it's
    /// non-refreshable by design). Held in memory only; a reload or expiry returns the staff user to
    /// their own identity via the untouched refresh cookie.
    /// </summary>
    public void BeginImpersonation(string accessToken)
    {
        _accessToken = accessToken;
        _isStaff = null;
    }

    /// <summary>
    /// Leaves an impersonated session and restores the staff user from their refresh cookie/store.
    /// Returns true when the original identity was restored.
    /// </summary>
    public async Task<bool> StopImpersonationAsync()
    {
        _accessToken = null;
        _isStaff = null;
        return await TryRefreshAsync();
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (sessionStore.UsesBodyTransport)
            {
                var stored = await sessionStore.GetRefreshTokenAsync();
                await httpClient.PostAsJsonAsync("/api/auth/logout", new { refresh_token = stored });
            }
            else
            {
                await httpClient.PostAsync("/api/auth/logout", null);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call logout endpoint");
        }

        await ClearSessionAsync();
    }

    /// <summary>Sets the in-memory access token and persists the rotated refresh token (native).</summary>
    private async Task AcceptTokensAsync(TokenResponse payload)
    {
        _accessToken = payload.AccessToken;
        _isStaff = null; // identity may have changed; re-probe on demand
        if (sessionStore.UsesBodyTransport && !string.IsNullOrEmpty(payload.RefreshToken))
            await sessionStore.SaveRefreshTokenAsync(payload.RefreshToken);
    }

    private async Task ClearSessionAsync()
    {
        _accessToken = null;
        _isStaff = null;
        if (sessionStore.UsesBodyTransport)
            await sessionStore.ClearAsync();
    }

    /// <summary>
    /// Resolves the session on app startup: with no token in memory, silently
    /// exchanges the HttpOnly refresh cookie for an access token. Called ONCE from
    /// MainLayout — the single auth entry point (mirrors phase2). Because the layout
    /// awaits this before rendering @Body, pages never trigger their own refresh.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (IsAuthenticated) return;
        await TryRefreshAsync();
    }

    /// <summary>Display name from the JWT 'name' claim, falling back to email.</summary>
    public string? DisplayName => Claim("name") ?? Claim(ClaimTypes.Name) ?? Claim("email") ?? Claim(ClaimTypes.Email);

    /// <summary>Tenant ("household") name from the JWT.</summary>
    public string? TenantName => Claim(AppClaims.TenantName);

    /// <summary>The user's saved UI locale from the JWT (e.g. "es"), or null if unset.</summary>
    public string? Locale => Claim(AppClaims.Locale);

    private string? Claim(string type)
    {
        if (string.IsNullOrEmpty(_accessToken) || IsTokenExpired(_accessToken))
            return null;
        try
        {
            return new JwtSecurityTokenHandler().ReadJwtToken(_accessToken)
                .Claims.FirstOrDefault(c => c.Type == type)?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTokenExpired(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return true;
        }
    }

    // GET /api/admin/me — is the caller platform staff?
    private sealed record StaffStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("is_staff")]
        public bool IsStaff { get; init; }
    }

    // GET /api/auth/providers — the OAuth providers this deployment configured.
    private sealed record ProvidersResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("providers")]
        public IReadOnlyList<string>? Providers { get; init; }
    }

    // A native primary-auth response: either tokens, or an MFA challenge to step up (mfa_required).
    private sealed record NativeAuthResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("mfa_required")]
        public bool MfaRequired { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("challenge")]
        public string? Challenge { get; init; }
    }

    // Mirrors the API's TokenResponse (snake_case JSON).
    private sealed record TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        // Present only for native clients; the web flow keeps the token in the cookie.
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
    }
}
