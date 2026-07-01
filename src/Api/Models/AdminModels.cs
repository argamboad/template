using System.Text.Json.Serialization;
using Template.Core.Repositories;

namespace Template.Api.Models;

public record AdminTenantSummaryResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("member_count")] public required int MemberCount { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }

    public static AdminTenantSummaryResponse From(TenantSummary t) =>
        new() { Id = t.Id, Name = t.Name, MemberCount = t.MemberCount, CreatedAt = t.CreatedAt };
}

public record AdminTenantDetailResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("members")] public required IReadOnlyList<TenantMemberResponse> Members { get; init; }
    [JsonPropertyName("subscription_status")] public required string SubscriptionStatus { get; init; }
    [JsonPropertyName("audit_event_count")] public required int AuditEventCount { get; init; }
}

/// <summary>Whether the authenticated caller is platform staff — drives the client's admin nav/gate.</summary>
public record AdminStatusResponse
{
    [JsonPropertyName("is_staff")] public required bool IsStaff { get; init; }
}

public record ImpersonationResponse
{
    /// <summary>Short-lived access token for the impersonated user. No refresh token is issued.</summary>
    [JsonPropertyName("access_token")] public required string AccessToken { get; init; }
    [JsonPropertyName("expires_in")] public required int ExpiresIn { get; init; }
}
