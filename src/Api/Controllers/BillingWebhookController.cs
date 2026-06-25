using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Services;

namespace Template.Api.Controllers;

/// <summary>
/// Receives billing-provider webhooks (BILLING-3, ADR-006). A <b>system</b> endpoint — the provider
/// calls it with no JWT — so it does NOT extend <c>TenantApiControllerBase</c>; authenticity is the
/// provider <b>signature</b>, verified inside <see cref="BillingWebhookHandler"/>, which then enters the
/// tenant context to apply the change safely (ADR-003). Anonymous + not rate-limited (the passwordless
/// limiter is opt-in per endpoint).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/billing/webhook")]
public class BillingWebhookController(BillingWebhookHandler handler) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        var result = await handler.HandleAsync(payload, signature, cancellationToken);
        return result == WebhookResult.InvalidSignature
            ? BadRequest(new { error = "invalid_signature" })
            : Ok(); // Applied / Duplicate / Ignored all acknowledge, so the provider stops retrying
    }
}
