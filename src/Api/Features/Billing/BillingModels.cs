namespace Template.Api.Features.Billing;

/// <summary>Request to start a hosted checkout for a paid plan.</summary>
public record CreateCheckoutRequest(string? PlanKey);

/// <summary>The hosted checkout URL the client should redirect to.</summary>
public record CheckoutResponse(string Url);
