using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Repository abstraction for refresh token persistence.
/// Separates token storage concerns from validation logic.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token);
    Task<RefreshToken?> GetValidTokenByHashAsync(string tokenHash);
    Task RevokeAsync(Guid tokenId);
    Task RevokeAllForUserAsync(Guid userId);
}
