using Microsoft.AspNetCore.Http;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

/// <summary>
/// The refresh cookie is pinned to <c>Path=/api/auth</c>. A legacy build that wrote it at a broader
/// path (<c>/</c>) leaves an orphan the browser keeps alongside the real one and sends on every
/// refresh — and the server reads only one, so the stale orphan can silently shadow the live token
/// and wedge sign-in (seen in Firefox, whose cookie send-order surfaces it). To self-heal, both
/// issuing and clearing the cookie must ALSO expire any <c>Path=/</c> orphan, so the next successful
/// sign-in (or logout) removes it with no manual cookie-clearing.
/// </summary>
public class CookieServiceTests
{
    private static CookieService Sut() => new(new TestRefreshSettings());

    private static string[] SetCookies(HttpResponse response) =>
        response.Headers.SetCookie.Select(h => h ?? "").ToArray();

    private static bool ApiAuthPath(string h) =>
        h.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase);

    // The legacy orphan delete is scoped to the root path (path=/), not /api/auth.
    private static bool RootPath(string h) =>
        h.Contains("path=/", StringComparison.OrdinalIgnoreCase) && !ApiAuthPath(h);

    [Fact]
    public void SetRefreshTokenCookie_IssuesAtApiAuth_AndSweepsLegacyRootPathCookie()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";

        Sut().SetRefreshTokenCookie(ctx.Response, "the-token", ctx.Request);
        var cookies = SetCookies(ctx.Response);

        // The real cookie carries the token and is scoped to /api/auth.
        Assert.Contains(cookies, h => h.StartsWith("refresh_token=the-token", StringComparison.Ordinal) && ApiAuthPath(h));
        // The self-heal: an expiry (empty value) for the legacy root-path orphan.
        Assert.Contains(cookies, h => h.StartsWith("refresh_token=;", StringComparison.Ordinal) && RootPath(h));
    }

    [Fact]
    public void DeleteRefreshTokenCookie_ExpiresTheApiAuthCookie()
    {
        // Logout clears the real cookie at its own path. (It does NOT also delete Path=/ — ASP.NET
        // would dedupe the two same-name Set-Cookie headers and drop this one; the legacy orphan is
        // swept by the next SetRefreshTokenCookie instead.)
        var ctx = new DefaultHttpContext();

        Sut().DeleteRefreshTokenCookie(ctx.Response);
        var cookies = SetCookies(ctx.Response);

        Assert.Contains(cookies, h => h.StartsWith("refresh_token=;", StringComparison.Ordinal) && ApiAuthPath(h));
    }
}
