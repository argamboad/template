using Template.Api.Configuration;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>
/// Result of issuing a refresh token. The raw token goes to the client cookie;
/// only its hash is persisted, so a database leak cannot be used to forge cookies.
/// </summary>
public record IssuedRefreshToken(string RawToken, RefreshToken Token);

public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, string ipAddress, string provider, CancellationToken cancellationToken = default);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages refresh token lifecycle. Delegates generation to ITokenGenerator,
/// hashing to ITokenHasher, and persistence to IRefreshTokenRepository.
/// </summary>
public class RefreshTokenService(
    IRefreshTokenRepository repository,
    ITokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IRefreshTokenSettings settings,
    TimeProvider clock) : IRefreshTokenService
{
    public async Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, string ipAddress, string provider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            ipAddress = "unknown";
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty", nameof(provider));

        var rawToken = tokenGenerator.GenerateToken();
        var tokenHash = tokenHasher.HashToken(rawToken);
        var now = clock.GetUtcNow();

        var refreshToken = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            IssuedAt = now,
            ExpiresAt = now.AddDays(settings.ExpiryDays),
            IsRevoked = false,
            IssuedFromIp = ipAddress,
            Provider = provider
        };

        var created = await repository.CreateAsync(refreshToken, cancellationToken);
        return new IssuedRefreshToken(rawToken, created);
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawToken))
            return null;

        var tokenHash = tokenHasher.HashToken(rawToken);
        return await repository.GetValidTokenByHashAsync(tokenHash, cancellationToken);
    }

    public Task RevokeRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken = default) => repository.RevokeAsync(tokenId, cancellationToken);

    public Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default) => repository.RevokeAllForUserAsync(userId, cancellationToken);
}
