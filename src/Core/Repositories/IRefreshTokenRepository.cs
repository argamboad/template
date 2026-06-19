using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Repository abstraction for refresh token persistence.
/// Separates token storage concerns from validation logic.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetValidTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
