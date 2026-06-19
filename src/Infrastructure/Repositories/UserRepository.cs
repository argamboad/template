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
    public async Task<User?> GetByLoginAsync(string provider, string providerUserId)
    {
        return await db.UserLogins
            .Where(l => l.Provider == provider && l.ProviderUserId == providerUserId)
            .Select(l => l.User)
            .FirstOrDefaultAsync();
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        return await db.Users.FindAsync(userId);
    }

    public async Task<User> CreateAsync(User user)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateAsync(User user)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task AddLoginAsync(UserLogin login)
    {
        db.UserLogins.Add(login);
        await db.SaveChangesAsync();
    }
}
