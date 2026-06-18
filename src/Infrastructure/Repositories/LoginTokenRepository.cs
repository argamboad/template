using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

public class LoginTokenRepository(AppDbContext db) : ILoginTokenRepository
{
    public async Task AddAsync(LoginToken token)
    {
        db.LoginTokens.Add(token);
        await db.SaveChangesAsync();
    }

    public async Task<LoginToken?> GetActiveByHashAsync(string email, string purpose, string codeHash)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.LoginTokens.FirstOrDefaultAsync(t =>
            t.Email == email && t.Purpose == purpose && t.CodeHash == codeHash
            && t.ConsumedAt == null && t.ExpiresAt > now);
    }

    public async Task<LoginToken?> GetLatestActiveAsync(string email, string purpose)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.LoginTokens
            .Where(t => t.Email == email && t.Purpose == purpose && t.ConsumedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateAsync(LoginToken token)
    {
        db.LoginTokens.Update(token);
        await db.SaveChangesAsync();
    }

    public async Task InvalidateActiveAsync(string email, string purpose)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.LoginTokens
            .Where(t => t.Email == email && t.Purpose == purpose && t.ConsumedAt == null)
            .ToListAsync();

        if (active.Count == 0) return;
        foreach (var t in active) t.ConsumedAt = now;
        await db.SaveChangesAsync();
    }
}
