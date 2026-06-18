using Template.Core.Entities;

namespace Template.Core.Repositories;

/// <summary>
/// Reads/removes the OAuth identities linked to an account.
/// </summary>
public interface IUserLoginRepository
{
    Task<List<UserLogin>> GetForUserAsync(Guid userId);
    Task<UserLogin?> GetByProviderForUserAsync(Guid userId, string provider);
    Task DeleteAsync(UserLogin login);
}
