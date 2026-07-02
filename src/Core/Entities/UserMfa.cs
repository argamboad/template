namespace Template.Core.Entities;

/// <summary>
/// A user's TOTP multi-factor state (MFA-1, ADR-012). One row per user. The TOTP secret is stored
/// <b>encrypted</b> (Data Protection) — never in plaintext, never returned after enrollment.
/// <see cref="Enabled"/> flips true only after a valid code confirms possession. User-scoped identity
/// data — wiped by account erasure (GDPR-2).
/// </summary>
public class UserMfa
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>The Data-Protection-encrypted TOTP secret. Never store or expose the plaintext.</summary>
    public required string EncryptedSecret { get; set; }

    /// <summary>True once enrollment is confirmed with a valid code.</summary>
    public bool Enabled { get; set; }

    public DateTimeOffset EnrolledAt { get; set; }

    /// <summary>
    /// The TOTP time-step accepted by the most recent successful <em>login</em> verification (MFA-2).
    /// A code whose step is ≤ this is rejected as a replay (RFC-6238 anti-replay; v2 audit LOGIC-S1).
    /// Null until the first login step-up; enrollment-confirm deliberately does not set it.
    /// </summary>
    public long? LastVerifiedTimeStep { get; set; }
}
