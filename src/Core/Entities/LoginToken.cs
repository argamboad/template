namespace Template.Core.Entities;

/// <summary>
/// A single-use, time-limited credential for passwordless sign-in — either a
/// magic-link token (long random value emailed as a URL) or an OTP (short numeric
/// code emailed to the user). Only the hash is stored, never the raw value.
/// </summary>
public class LoginToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Normalized (lower-cased) email the credential was issued to.</summary>
    public required string Email { get; set; }

    /// <summary>SHA-256 hash of the raw token/code.</summary>
    public required string CodeHash { get; set; }

    /// <summary><see cref="LoginTokenPurpose"/> — magic link or OTP.</summary>
    public required string Purpose { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the credential is redeemed (or locked out); null while usable.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Failed verification attempts — used to lock out OTP brute-forcing.</summary>
    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Derived, never stored.
    public bool IsConsumed => ConsumedAt.HasValue;
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsValid => !IsConsumed && !IsExpired;
}

/// <summary>Discriminates the kind of one-time credential.</summary>
public static class LoginTokenPurpose
{
    public const string MagicLink = "magic-link";
    public const string Otp = "otp";
}
