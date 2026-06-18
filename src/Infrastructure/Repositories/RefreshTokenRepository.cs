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
    public async Task<RefreshToken> CreateAsync(RefreshToken token)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    public async Task<RefreshToken?> GetValidTokenByHashAsync(string tokenHash)
    {
        return await db.RefreshTokens.FirstOrDefaultAsync(t =>
            t.TokenHash == tokenHash &&
            !t.IsRevoked &&
            t.ExpiresAt > DateTime.UtcNow);
    }

    public async Task RevokeAsync(Guid tokenId)
    {
        var token = await db.RefreshTokens.FindAsync(tokenId);
        if (token != null)
        {
            token.IsRevoked = true;
            await db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
        }

        if (tokens.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }
}
