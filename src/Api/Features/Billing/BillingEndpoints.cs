using System.Security.Claims;

namespace Template.Api.Features.Billing;

/// <summary>
/// Billing feature endpoints (ADR-006). Registered with <c>app.MapBilling()</c> in <c>Program.cs</c>.
/// Uses <c>MapTenantFeatureGroup</c> so the shared tenant-API auth policy applies; the owner-only check
/// is enforced in <see cref="BillingHandler"/>.
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBilling(this IEndpointRouteBuilder app)
    {
        var group = app.MapTenantFeatureGroup("/api/billing");

        group.MapPost("/checkout", async (CreateCheckoutRequest request, ClaimsPrincipal user, BillingHandler handler, CancellationToken ct) =>
        {
            var userId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
            var result = await handler.CreateCheckoutAsync(userId, request.PlanKey, ct);
            return result.Status switch
            {
                CheckoutStatus.Created => Results.Ok(new CheckoutResponse(result.Url!)),
                CheckoutStatus.Forbidden => Results.Json(
                    new { error = "forbidden", message = "Only the household owner can manage billing" },
                    statusCode: StatusCodes.Status403Forbidden),
                CheckoutStatus.InvalidPlan => Results.BadRequest(
                    new { error = "invalid_plan", message = "Unknown or non-purchasable plan" }),
                _ => Results.Json(
                    new { error = "invalid_token", message = "Invalid user identity" },
                    statusCode: StatusCodes.Status401Unauthorized),
            };
        });

        return app;
    }
}
