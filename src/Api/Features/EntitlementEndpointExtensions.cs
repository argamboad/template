using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Configuration;
using Template.Core.Abstractions;

namespace Template.Api.Features;

/// <summary>
/// Gates an endpoint (or a feature group) behind a plan entitlement (ADR-006). A feature slice adds
/// <c>.RequireEntitlement(Entitlements.Xxx)</c> alongside <see cref="FeatureEndpointExtensions.MapTenantFeatureGroup"/>
/// to declare a paid gate; a tenant whose plan lacks the entitlement gets <b>402 Payment Required</b>
/// (not 403/500), with the entitlement named and an upgrade link. The check is server-side via
/// <see cref="IEntitlementService"/> — never trust the client.
/// </summary>
public static class EntitlementEndpointExtensions
{
    public static TBuilder RequireEntitlement<TBuilder>(this TBuilder builder, string entitlementKey)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(async (context, next) =>
        {
            var entitlements = context.HttpContext.RequestServices.GetRequiredService<IEntitlementService>();
            if (await entitlements.HasAsync(entitlementKey, context.HttpContext.RequestAborted))
                return await next(context);

            var app = context.HttpContext.RequestServices.GetRequiredService<IApplicationSettings>();
            return Results.Json(new
            {
                error = "payment_required",
                entitlement = entitlementKey,
                message = $"This feature requires a plan that includes '{entitlementKey}'.",
                upgrade_url = $"{app.ClientUrl.TrimEnd('/')}/billing",
            }, statusCode: StatusCodes.Status402PaymentRequired);
        });
}
