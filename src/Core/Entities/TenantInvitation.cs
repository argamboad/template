namespace Template.Core.Entities;

/// <summary>
/// An invitation to join a tenant. Created by the tenant owner, addressed to an
/// email, redeemed by whoever is signed in and presents the single-use token
/// (identity is the login, not the bare email). Only the SHA-256 hash of the
/// token is stored — the raw token is revealed once at creation.
/// </summary>
public class TenantInvitation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    /// <summary>Normalized lower-case email the invite was addressed to.</summary>
    public required string InvitedEmail { get; set; }

    public Guid InvitedByUserId { get; set; }

    /// <summary>One of <see cref="InvitationStatuses"/>.</summary>
    public string Status { get; set; } = InvitationStatuses.Pending;

    /// <summary>SHA-256 hash of the single-use token. Raw token revealed once at creation.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // Derived — computed, never stored.
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsValid => Status == InvitationStatuses.Pending && !IsExpired;
}

/// <summary>Status values for <see cref="TenantInvitation.Status"/>.</summary>
public static class InvitationStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}
