using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Template.Api.Services;

/// <summary>
/// Short-lived, single-use tokens that carry "link this OAuth identity to
/// user X" through the provider round-trip. A browser navigation to the OAuth
/// challenge can't carry the JWT, hence the token.
/// </summary>
public interface ILinkTokenService
{
    string Issue(Guid userId);

    /// <summary>Returns the user id and consumes the token; null if unknown/expired/used.</summary>
    Guid? Redeem(string token);
}

public class LinkTokenService(IMemoryCache cache) : ILinkTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public string Issue(Guid userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        cache.Set(CacheKey(token), userId, Lifetime);
        return token;
    }

    public Guid? Redeem(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var key = CacheKey(token);
        if (!cache.TryGetValue(key, out Guid userId))
            return null;

        cache.Remove(key);   // single-use
        return userId;
    }

    private static string CacheKey(string token) => $"link-token:{token}";
}
