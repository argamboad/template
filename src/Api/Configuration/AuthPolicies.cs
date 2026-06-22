using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Template.Api.Configuration;

/// <summary>
/// The single definition of the tenant-API authorization policy. Both the platform controllers
/// (<c>TenantApiControllerBase</c> via <c>[Authorize(AuthPolicies.TenantApi)]</c>) and feature
/// minimal-API groups (<c>MapTenantFeatureGroup</c>) reference this one named policy, so the
/// "authenticated via the app JWT" rule is defined once and can't drift between the two styles
/// (CONF-3).
/// </summary>
public static class AuthPolicies
{
    public const string TenantApi = "TenantApi";

    public static IServiceCollection AddTenantApiAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
            options.AddPolicy(TenantApi, policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)));
}
