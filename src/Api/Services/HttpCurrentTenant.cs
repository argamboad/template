using System.Security.Claims;
using Template.Core.Abstractions;

namespace Template.Api.Services;

/// <summary>
/// Resolves <see cref="ICurrentTenant"/> from the authenticated principal's
/// <c>tenant_id</c> claim (issued in the JWT). Registered request-scoped; returns null
/// when there is no authenticated user or tenant claim, which makes tenant-scoped
/// queries return nothing (fail closed).
/// </summary>
public sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public Guid? TenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtClaims.TenantId)?.Value, out var id)
            ? id
            : null;
}
