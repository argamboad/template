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
