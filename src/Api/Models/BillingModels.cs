using System.Text.Json.Serialization;

namespace Template.Api.Models;

/// <summary>Request to start a hosted checkout for a paid plan.</summary>
public record CreateCheckoutRequest
{
    [JsonPropertyName("plan_key")] public string? PlanKey { get; init; }
}

/// <summary>The hosted checkout URL the client should redirect to.</summary>
public record CheckoutResponse
{
    [JsonPropertyName("url")] public required string Url { get; init; }
}
