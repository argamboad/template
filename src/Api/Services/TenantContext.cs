using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>
/// Resolves the authenticated caller's tenant id once per request. App-data
/// controllers scope every read/write by this id instead of the user id.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The caller's tenant id, or null when they have no membership (controllers
    /// then return 401/403 — never an unscoped query).
    /// </summary>
    Task<Guid?> GetTenantIdAsync(Guid userId);
}

/// <summary>Scoped resolver — caches the lookup for the lifetime of one request.</summary>
public class TenantContext(ITenantRepository tenants) : ITenantContext
{
    private Guid? _cached;
    private bool _resolved;

    public async Task<Guid?> GetTenantIdAsync(Guid userId)
    {
        if (_resolved) return _cached;
        _cached = await tenants.GetTenantIdForUserAsync(userId);
        _resolved = true;
        return _cached;
    }
}
