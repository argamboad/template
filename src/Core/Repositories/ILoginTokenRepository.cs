using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Persists single-use passwordless credentials (magic-link tokens and OTP codes).
/// </summary>
public interface ILoginTokenRepository
{
    Task AddAsync(LoginToken token);

    /// <summary>An unconsumed, unexpired credential matching the exact hash (magic-link lookup).</summary>
    Task<LoginToken?> GetActiveByHashAsync(string email, string purpose, string codeHash);

    /// <summary>The most recent unconsumed, unexpired credential for the email+purpose (OTP lookup).</summary>
    Task<LoginToken?> GetLatestActiveAsync(string email, string purpose);

    Task UpdateAsync(LoginToken token);

    /// <summary>Consumes every still-active credential of this purpose — called before issuing a new one.</summary>
    Task InvalidateActiveAsync(string email, string purpose);
}
