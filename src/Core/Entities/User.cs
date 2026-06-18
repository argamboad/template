namespace Template.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }

    /// <summary>Display name from the OAuth provider, refreshed each sign-in.</summary>
    public string? DisplayName { get; set; }

    /// <summary>True only when the provider asserts a verified email claim.</summary>
    public bool EmailVerified { get; set; }

    // Tenant membership is the source of truth for which tenant a user belongs to;
    // resolve it via TenantMembership (one tenant per user). See ITenantRepository.

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>OAuth identities linked to this account (one per provider).</summary>
    public ICollection<UserLogin> Logins { get; set; } = new List<UserLogin>();
}
