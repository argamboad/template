using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Persists single-use passwordless credentials (magic-link tokens and OTP codes).
/// </summary>
public interface ILoginTokenRepository
{
    Task AddAsync(LoginToken token, CancellationToken cancellationToken = default);

    /// <summary>An unconsumed, unexpired credential matching the exact hash (magic-link lookup).</summary>
    Task<LoginToken?> GetActiveByHashAsync(string email, string purpose, string codeHash, CancellationToken cancellationToken = default);

    /// <summary>The most recent unconsumed, unexpired credential for the email+purpose (OTP lookup).</summary>
    Task<LoginToken?> GetLatestActiveAsync(string email, string purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sum of failed attempts across ALL credentials of this purpose issued to the email since
    /// <paramref name="since"/> — including consumed/rotated-out ones. Drives the cumulative,
    /// resend-proof OTP lockout (a fresh code can't reset the budget).
    /// </summary>
    Task<int> CountFailedAttemptsSinceAsync(string email, string purpose, DateTimeOffset since, CancellationToken cancellationToken = default);

    Task UpdateAsync(LoginToken token, CancellationToken cancellationToken = default);

    /// <summary>Consumes every still-active credential of this purpose — called before issuing a new one.</summary>
    Task InvalidateActiveAsync(string email, string purpose, CancellationToken cancellationToken = default);
}
