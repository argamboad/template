using Microsoft.AspNetCore.Mvc;
using Template.Api.Models;
using Template.Api.Services;
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
    IErrorResponseFactory errorFactory,
    IBillingService billing) : TenantApiControllerBase(tenants, errorFactory)
{
    /// <summary>Starts a hosted checkout to subscribe the tenant to a paid plan (owner only).</summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CreateCheckoutRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync(cancellationToken);
        if (membership is null)
            return InvalidToken();
        if (!IsOwner(membership))
            return Forbid403("Only the household owner can manage billing");

        var result = await billing.CreateCheckoutAsync(membership.TenantId, request.PlanKey, cancellationToken);
        return result.Outcome switch
        {
            CheckoutOutcome.Created => Ok(new CheckoutResponse { Url = result.Url! }),
            _ => BadRequest(ErrorFactory.CreateError("invalid_plan", "Unknown or non-purchasable plan")),
        };
    }
}
