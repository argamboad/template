using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Template.Shared.Ui.Auth;

/// <summary>
/// Client-side authentication with refresh-token support.
/// The access token is held in memory only — never persisted to localStorage.
/// Sessions survive reloads/restarts via the HttpOnly refresh cookie: on startup
/// we silently exchange it for a fresh access token.
/// </summary>
public class AuthService(HttpClient httpClient, ILogger<AuthService> logger)
{
    private string? _accessToken;
    private Task<bool>? _refreshInFlight;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && !IsTokenExpired(_accessToken);

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
            // The CookieHandler in the HTTP pipeline includes browser credentials
            // (the cross-origin HttpOnly refresh cookie) on every request.
            var response = await httpClient.PostAsync("/api/auth/refresh", null);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Token refresh failed: {StatusCode}", response.StatusCode);
                _accessToken = null;
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (!string.IsNullOrEmpty(payload?.AccessToken))
            {
                _accessToken = payload.AccessToken;
                return true;
            }

            logger.LogWarning("Refresh response missing access_token");
            _accessToken = null;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh access token");
            _accessToken = null;
            return false;
        }
        finally
        {
            // Allow a fresh refresh next time; only *concurrent* calls are coalesced.
            _refreshInFlight = null;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await httpClient.PostAsync("/api/auth/logout", null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call logout endpoint");
        }

        _accessToken = null;
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

    // Mirrors the API's TokenResponse (snake_case JSON).
    private sealed record TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }
}
