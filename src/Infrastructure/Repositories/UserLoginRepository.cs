using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Repositories;

public class UserLoginRepository(AppDbContext db) : IUserLoginRepository
{
    public async Task<List<UserLogin>> GetForUserAsync(Guid userId)
    {
        return await db.UserLogins
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<UserLogin?> GetByProviderForUserAsync(Guid userId, string provider)
    {
        return await db.UserLogins
            .FirstOrDefaultAsync(l => l.UserId == userId && l.Provider == provider);
    }

    public async Task DeleteAsync(UserLogin login)
    {
        db.UserLogins.Remove(login);
        await db.SaveChangesAsync();
    }
}
