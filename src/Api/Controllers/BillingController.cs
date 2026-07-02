using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Models;
using Template.Api.Services;
using Template.Core.Authorization;
using Template.Core.Repositories;

namespace Template.Api.Controllers;

/// <summary>
/// Billing &amp; subscriptions (ADR-006). A <b>platform</b> controller — billing is reusable chassis,
/// not an app feature — so it lives alongside the household controllers and reuses the
/// owner/membership gate from <see cref="TenantApiControllerBase"/> rather than re-implementing it.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController(
    ITenantRepository tenants,
    IBillingService billing) : TenantApiControllerBase(tenants)
{
    /// <summary>Starts a hosted checkout to subscribe the tenant to a paid plan (owner only).</summary>
    [HttpPost("checkout")]
    [RequireTenantPermission(Permission.ManageBilling, "Only the household owner can manage billing")]
    public async Task<IActionResult> Checkout([FromBody] CreateCheckoutRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership is null)
            return InvalidToken();

        var result = await billing.CreateCheckoutAsync(membership.TenantId, request.PlanKey, cancellationToken);
        return result.Outcome switch
        {
            CheckoutOutcome.Created => Ok(new CheckoutResponse { Url = result.Url! }),
            _ => BadRequest(new ErrorResponse("invalid_plan", "Unknown or non-purchasable plan")),
        };
    }

    /// <summary>Opens the hosted billing-management portal for the tenant's subscription (owner only).</summary>
    [HttpPost("portal")]
    [RequireTenantPermission(Permission.ManageBilling, "Only the household owner can manage billing")]
    public async Task<IActionResult> Portal(CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership is null)
            return InvalidToken();

        var result = await billing.CreatePortalAsync(cancellationToken);
        return result.Outcome switch
        {
            PortalOutcome.Created => Ok(new PortalResponse { Url = result.Url! }),
            _ => BadRequest(new ErrorResponse("no_subscription", "No subscription to manage")),
        };
    }
}
