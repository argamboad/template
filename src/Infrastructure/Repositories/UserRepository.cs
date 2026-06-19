using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUserRepository"/>.
/// Encapsulates all user data access logic.
/// </summary>
public class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> GetByLoginAsync(string provider, string providerUserId, CancellationToken cancellationToken = default)
    {
        return await db.UserLogins
            .Where(l => l.Provider == provider && l.ProviderUserId == providerUserId)
            .Select(l => l.User)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await db.Users.FindAsync([userId], cancellationToken);
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task AddLoginAsync(UserLogin login, CancellationToken cancellationToken = default)
    {
        db.UserLogins.Add(login);
        await db.SaveChangesAsync(cancellationToken);
    }
}
