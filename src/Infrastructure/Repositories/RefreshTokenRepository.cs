using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenRepository"/>.
/// Encapsulates all refresh token persistence logic.
/// </summary>
public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshToken?> GetValidTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await db.RefreshTokens.FirstOrDefaultAsync(t =>
            t.TokenHash == tokenHash &&
            !t.IsRevoked &&
            t.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await db.RefreshTokens.FindAsync([tokenId], cancellationToken);
        if (token != null)
        {
            token.IsRevoked = true;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
        }

        if (tokens.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
