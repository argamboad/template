using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

public class LoginTokenRepository(AppDbContext db, TimeProvider clock) : ILoginTokenRepository
{
    public async Task AddAsync(LoginToken token, CancellationToken cancellationToken = default)
    {
        db.LoginTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginToken?> GetActiveByHashAsync(string email, string purpose, string codeHash, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return await db.LoginTokens.FirstOrDefaultAsync(t =>
            t.Email == email && t.Purpose == purpose && t.CodeHash == codeHash
            && t.ConsumedAt == null && t.ExpiresAt > now, cancellationToken);
    }

    public async Task<LoginToken?> GetLatestActiveAsync(string email, string purpose, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return await db.LoginTokens
            .Where(t => t.Email == email && t.Purpose == purpose && t.ConsumedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountFailedAttemptsSinceAsync(string email, string purpose, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        // (int?) + ?? 0 so SUM over zero rows yields 0 rather than throwing on a NULL aggregate.
        await db.LoginTokens
            .Where(t => t.Email == email && t.Purpose == purpose && t.CreatedAt >= since)
            .SumAsync(t => (int?)t.AttemptCount, cancellationToken) ?? 0;

    public async Task UpdateAsync(LoginToken token, CancellationToken cancellationToken = default)
    {
        db.LoginTokens.Update(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateActiveAsync(string email, string purpose, CancellationToken cancellationToken = default) =>
        // Set-based: consume every still-active credential in one statement (standalone commit).
        await db.LoginTokens
            .Where(t => t.Email == email && t.Purpose == purpose && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, (DateTimeOffset?)clock.GetUtcNow()), cancellationToken);
}
