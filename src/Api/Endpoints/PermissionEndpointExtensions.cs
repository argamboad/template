using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Authorization;

namespace Perezosoft.Api.Endpoints;

/// <summary>
/// Gates an endpoint (or a feature group) behind a tenant <see cref="Permission"/> (ADR-009). A feature
/// slice adds <c>.RequirePermission(Permission.Xxx)</c> to declare an authorization gate; a caller whose
/// role lacks the permission gets <b>403 Forbidden</b> — the authorization counterpart to
/// <see cref="EntitlementEndpointExtensions.RequireEntitlement{TBuilder}"/>'s 402 (payment). The check is
/// server-side via <see cref="IPermissionService"/> (the role→permission matrix) — never trust the client.
/// </summary>
public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(async (context, next) =>
        {
            var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            if (await permissions.HasAsync(permission, context.HttpContext.RequestAborted))
                return await next(context);

            return Results.Json(new
            {
                error = "forbidden",
                permission = permission.ToString(),
                message = $"This action requires the '{permission}' permission.",
            }, statusCode: StatusCodes.Status403Forbidden);
        });
}
