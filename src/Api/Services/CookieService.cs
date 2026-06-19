using Template.Api.Configuration;

namespace Template.Api.Services;

/// <summary>
/// Manages refresh token cookie operations.
/// </summary>
public class CookieService(IRefreshTokenSettings settings) : ICookieService
{
    private const string RefreshTokenCookieName = "refresh_token";

    public void SetRefreshTokenCookie(HttpResponse response, string token, HttpRequest request)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be empty", nameof(token));

        // SameSite=Lax: the client (https://localhost:7008) and API (https://localhost:7160)
        // are the same site (localhost), so the refresh fetch is a same-site request and the
        // cookie is sent. Lax/Strict require both ends to be HTTPS (schemeful same-site treats
        // http and https localhost as different sites). Do NOT use None here — None demands
        // Secure and is dropped whenever any leg isn't HTTPS, which silently breaks refresh.
        response.Cookies.Append(RefreshTokenCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(settings.ExpiryDays)
        });
    }

    public void DeleteRefreshTokenCookie(HttpResponse response)
    {
        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = "/api/auth"
        });
    }

    public string? GetRefreshTokenFromCookies(HttpRequest request)
    {
        request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken);
        return refreshToken;
    }
}
