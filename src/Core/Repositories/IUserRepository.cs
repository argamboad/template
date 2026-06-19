using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Repository abstraction for user data access.
/// Provider identities live in user logins; a user can have several.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByLoginAsync(string provider, string providerUserId);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(Guid userId);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task AddLoginAsync(UserLogin login);
}
