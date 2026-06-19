using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Template.Api.Services;

/// <summary>The identity carried by a redeemed native-auth code.</summary>
public readonly record struct NativeAuthGrant(Guid UserId, string Provider);

/// <summary>
/// Short-lived, single-use authorization codes for the native (desktop/mobile) OAuth
/// flow. The loopback/scheme redirect can carry only a code, never tokens (a token in
/// a URL leaks via history/logs) — mirroring the web flow's "JWT never in the URL"
/// rule. The app exchanges the code for tokens via POST /api/auth/native/exchange.
/// </summary>
public interface INativeAuthCodeService
{
    /// <summary>Issues a single-use code bound to the user + provider.</summary>
    string Issue(Guid userId, string provider);

    /// <summary>Returns the grant and consumes the code; null if unknown/expired/used.</summary>
    NativeAuthGrant? Redeem(string code);
}

public class NativeAuthCodeService(IMemoryCache cache) : INativeAuthCodeService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public string Issue(Guid userId, string provider)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        cache.Set(CacheKey(code), new NativeAuthGrant(userId, provider), Lifetime);
        return code;
    }

    public NativeAuthGrant? Redeem(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var key = CacheKey(code);
        if (!cache.TryGetValue(key, out NativeAuthGrant grant))
            return null;

        cache.Remove(key);   // single-use
        return grant;
    }

    private static string CacheKey(string code) => $"native-auth-code:{code}";
}
